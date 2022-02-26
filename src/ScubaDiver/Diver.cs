using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Reflection;
using System.Threading;
using Microsoft.Diagnostics.Runtime;
using Newtonsoft.Json;
using ScubaDiver.API;
using ScubaDiver.API.Dumps;
using ScubaDiver.API.Extensions;
using ScubaDiver.API.Utils;
using ScubaDiver.Utils;
using Exception = System.Exception;

namespace ScubaDiver
{

    public class Diver : IDisposable
    {
        // Runtime analysis and exploration fields
        private readonly object _debugObjectsLock = new();
        private DataTarget _dt = null;
        private ClrRuntime _runtime = null;
        private readonly Converter<object> _converter = new();

        // Clients Tracking
        public object _registeredPidsLock = new();
        public List<int> _registeredPids = new();

        // HTTP Responses fields
        private readonly Dictionary<string, Func<HttpListenerRequest, string>> _responseBodyCreators;

        // Pinning objects fields
        private readonly ConcurrentDictionary<ulong, FrozenObjectInfo> _pinnedObjects;

        // Callbacks Endpoint of the Controller process
        IPEndPoint _callbacksEndpoint;
        int _nextAvilableCallbackToken = 0;
        private readonly ConcurrentDictionary<int, RegisteredEventHandlerInfo> _remoteEventHandler;
        private readonly ConcurrentDictionary<int, RegisteredMethodHookInfo> _remoteHooks;

        private readonly ManualResetEvent _stayAlive = new(true);

        public bool HasCallbackEndpoint => _callbacksEndpoint != null;

        public Diver()
        {
            _responseBodyCreators = new Dictionary<string, Func<HttpListenerRequest, string>>()
            {
                {"/ping", MakePingResponse},
                {"/die", MakeDieResponse},
                {"/register_client", MakeRegisterClientResponse},
                {"/unregister_client", MakeUnregisterClientResponse},
                {"/domains", MakeDomainsResponse},
                {"/heap", MakeHeapResponse},
                {"/invoke", MakeInvokeResponse},
                {"/get_field", MakeGetFieldResponse},
                {"/set_field", MakeSetFieldResponse},
                {"/create_object", MakeCreateObjectResponse},
                {"/object", MakeObjectResponse},
                {"/unpin", MakeUnpinResponse},
                {"/types", MakeTypesResponse},
                {"/type", MakeTypeResponse},
                {"/register_callbacks_ep", MakeRegisterCallbacksEndpointResponse},
                {"/event_subscribe", MakeEventSubscribeResponse},
                {"/event_unsubscribe", MakeEventUnsubscribeResponse},
                {"/hook_method", MakeHookMethodResponse},
                {"/unhook_method", MakeUnhookMethodResponse},
                {"/get_item", MakeArrayItemResponse},
            };
            _pinnedObjects = new ConcurrentDictionary<ulong, FrozenObjectInfo>();
            _remoteEventHandler = new ConcurrentDictionary<int, RegisteredEventHandlerInfo>();
            _remoteHooks = new ConcurrentDictionary<int, RegisteredMethodHookInfo>();
        }

        private string MakePingResponse(HttpListenerRequest arg)
        {
            return "{\"status\":\"pong\"}";
        }

        private string MakeUnregisterClientResponse(HttpListenerRequest arg)
        {
            string pidString = arg.QueryString.Get("process_id");
            if (pidString == null || !int.TryParse(pidString, out int pid))
            {
                return QuickError("Missing parameter 'process_id'");
            }
            bool removed;
            int remaining;
            lock (_registeredPidsLock)
            {
                removed = _registeredPids.Remove(pid);
                remaining = _registeredPids.Count;
            }
            Logger.Debug("[Diver] Client unregistered. ID = " + pid);

            UnregisterClientResponse ucResponse = new()
            {
                WasRemvoed = removed,
                OtherClientsAmount = remaining
            };

            return JsonConvert.SerializeObject(ucResponse);
        }

        private string MakeRegisterClientResponse(HttpListenerRequest arg)
        {
            string pidString = arg.QueryString.Get("process_id");
            if (pidString == null || !int.TryParse(pidString, out int pid))
            {
                return QuickError("Missing parameter 'process_id'");
            }
            lock (_registeredPidsLock)
            {
                _registeredPids.Add(pid);
            }
            Logger.Debug("[Diver] New client registered. ID = " + pid);
            return "{\"status\":\"OK'\"}";
        }

        void RefreshRuntime()
        {
            lock (_debugObjectsLock)
            {
                _runtime?.Dispose();
                _runtime = null;
                _dt?.Dispose();
                _dt = null;

                // This works like 'fork()', it does NOT create a dump file and uses it as the target
                // Instead it creates a secondary process which is a copy of the current one, but without any running threads.
                // Then our process attaches to the other one and reads its memory.
                //
                // NOTE: This subprocess inherits handles to DLLs in the current process so it might "lock"
                // both UnmanagedAdapterDLL.dll and ScubaDiver.dll
                _dt = DataTarget.CreateSnapshotAndAttach(Process.GetCurrentProcess().Id);
                _runtime = _dt.ClrVersions.Single().CreateRuntime();
            }
        }

        public void Dive(ushort listenPort)
        {
            // Start session
            RefreshRuntime();
            HttpListener listener = new();
            string listeningUrl = $"http://127.0.0.1:{listenPort}/";
            listener.Prefixes.Add(listeningUrl);
            // Set timeout
            var manager = listener.TimeoutManager;
            manager.IdleConnection = TimeSpan.FromSeconds(5);
            listener.Start();
            Logger.Debug($"[Diver] Listening on {listeningUrl}...");

            Dispatcher(listener);

            Logger.Debug("[Diver] Closing listener");
            listener.Stop();
            listener.Close();
            Logger.Debug("[Diver] Closing ClrMD runtime and snapshot");
            lock (_debugObjectsLock)
            {
                _runtime?.Dispose();
                _runtime = null;
                _dt?.Dispose();
                _dt = null;
            }

            Logger.Debug("[Diver] Unpinning objects");
            foreach (ulong pinAddr in _pinnedObjects.Keys.ToList())
            {
                bool res = UnpinObject(pinAddr);
                Logger.Debug($"[Diver] Addr {pinAddr:X16} unpinning returned {res}");
            }

            Logger.Debug("[Diver] Dispatcher returned, Dive is complete.");
        }

        private void HandleDispatchedRequest(HttpListenerContext requestContext)
        {
            HttpListenerRequest request = requestContext.Request;

            var response = requestContext.Response;
            string body;
            if (_responseBodyCreators.TryGetValue(request.Url.AbsolutePath, out var respBodyGenerator))
            {
                try
                {
                    body = respBodyGenerator(request);
                }
                catch (Exception ex)
                {
                    body = QuickError($"Error when running command {request}.\nException:\n"+ex);
                }
            }
            else
            {
                body = QuickError("Unknown Command");
            }

            byte[] buffer = System.Text.Encoding.UTF8.GetBytes(body);
            // Get a response stream and write the response to it.
            response.ContentLength64 = buffer.Length;
            response.ContentType = "application/json";
            System.IO.Stream output = response.OutputStream;
            output.Write(buffer, 0, buffer.Length);
            // You must close the output stream.
            output.Close();
        }

        private void Dispatcher(HttpListener listener)
        {
            // Using a timeout we can make sure not to block if the
            // 'stayAlive' state changes to "reset" (which means we should die)
            while (_stayAlive.WaitOne(TimeSpan.FromMilliseconds(100)))
            {
                void ListenerCallback(IAsyncResult result)
                {
                    HttpListener listener = (HttpListener)result.AsyncState;
                    HttpListenerContext context;
                    try
                    {
                        context = listener.EndGetContext(result);
                    }
                    catch (ObjectDisposedException)
                    {
                        Logger.Debug("[Diver][ListenerCallback] Listener is disposed. Exiting.");
                        return;
                    }

                    try
                    {
                        HandleDispatchedRequest(context);
                    }
                    catch (Exception e)
                    {
                        Console.WriteLine("[Diver] Task faulted! Exception:");
                        Console.WriteLine(e);
                    }
                }
                IAsyncResult asyncOperation = listener.BeginGetContext(ListenerCallback, listener);

                while (true)
                {
                    if (asyncOperation.AsyncWaitHandle.WaitOne(TimeSpan.FromMilliseconds(100)))
                    {
                        // Async operation started! We can mov on to next request
                        break;
                    }
                    else
                    {
                        // Async event still awaiting new HTTP requests... It's a good time to check
                        // if we were signaled to die
                        if (!_stayAlive.WaitOne(TimeSpan.FromMilliseconds(100)))
                        {
                            // Time to die.
                            // Leaving the inner loop will get us to the outter loop where _stayAlive is checked (again)
                            // and then it that loop will stop as well.
                            break;
                        }
                        else
                        {
                            // No singal of die command. We can continue waiting
                            continue;
                        }
                    }
                }
            }

            Logger.Debug("[Diver] HTTP Loop ended. Cleaning up");

            Logger.Debug("[Diver] Removing all event subscriptions & hooks");
            foreach (RegisteredEventHandlerInfo rehi in _remoteEventHandler.Values)
            {
                rehi.EventInfo.RemoveEventHandler(rehi.Target, rehi.RegisteredProxy);
            }
            foreach (RegisteredMethodHookInfo rmhi in _remoteHooks.Values)
            {
                ScubaDiver.Hooking.HarmonyWrapper.Instance.RemovePrefix(rmhi.OriginalHookedMethod);
            }
            _remoteEventHandler.Clear();
            _remoteHooks.Clear();
            Logger.Debug("[Diver] Removed all event subscriptions & hooks");
        }


        public int AssignCallbackToken() => Interlocked.Increment(ref _nextAvilableCallbackToken);
        public void InvokeControllerCallback(int token, params object[] parameters)
        {
            ReverseCommunicator reverseCommunicator = new(_callbacksEndpoint);

            ObjectOrRemoteAddress[] remoteParams = new ObjectOrRemoteAddress[parameters.Length];
            for (int i = 0; i < parameters.Length; i++)
            {
                object parameter = parameters[i];
                if (parameter == null)
                {
                    remoteParams[i] = ObjectOrRemoteAddress.Null;
                }
                else if (parameter.GetType().IsPrimitiveEtc())
                {
                    remoteParams[i] = ObjectOrRemoteAddress.FromObj(parameter);
                }
                else // Not primitive
                {
                    if (!IsPinned(parameter, out FrozenObjectInfo foi))
                    {
                        // Pin and mark for unpinning later
                        foi = PinObject(parameter);
                    }

                    remoteParams[i] = ObjectOrRemoteAddress.FromToken(foi.Address, foi.Object.GetType().FullName);
                }
            }

            // Call callback at controller
            reverseCommunicator.InvokeCallback(token, remoteParams);
        }
        /// <summary>
        /// Tries to get a <see cref="FrozenObjectInfo"/> of a pinned object
        /// </summary>
        /// <param name="objAddress">Address of the object to check wether it's pinned</param>
        /// <param name="foi">The frozen object info</param>
        /// <returns>True if the object was pinned, False if it was not</returns>
        public bool TryGetPinnedObject(ulong objAddress, out FrozenObjectInfo foi)
        {
            return _pinnedObjects.TryGetValue(objAddress, out foi);
        }
        /// <summary>
        /// Checks if an object is pinned
        /// </summary>
        /// <param name="instance">The object to check</param>
        /// <param name="foi">The FrozenObjectInfo in case the object was pinned</param>
        /// <returns>True if it was pinned, False if it wasn't</returns>
        public bool IsPinned(object instance, out FrozenObjectInfo foi)
        {
            // TODO: There are more efficient ways to do this
            foreach (FrozenObjectInfo currFoi in _pinnedObjects.Values)
            {
                if (currFoi.Object == instance)
                {
                    foi = currFoi;
                    return true;
                }
            }
            foi = null;
            return false;
        }
        /// <summary>
        /// Unpins an object
        /// </summary>
        /// <returns>True if it was pinned, false if not.</returns>
        public bool UnpinObject(ulong objAddress)
        {
            if (_pinnedObjects.TryRemove(objAddress, out FrozenObjectInfo poi))
            {
                if (poi is FrozenObjectInfo foi)
                {
                    foi.UnfreezeEvent.Set();
                    foi.FreezeThread.Join();
                }
                return true;
            }
            return false;
        }
        /// <summary>
        /// Pins an object, possibly at a specific address.
        /// </summary>
        /// <param name="instance">The object to pin</param>
        /// <param name="requiredPinningAddress">Current objects address if keeping it is crucial or null if it doesn't matter</param>
        /// <returns></returns>
        public FrozenObjectInfo PinObject(object instance)
        {
            FrozenObjectInfo fObj = Freezer.Freeze(instance);
            _pinnedObjects.TryAdd(fObj.Address, fObj);

            return fObj;
        }

        private (bool anyErrors, List<HeapDump.HeapObject> objects) GetHeapObjects(Predicate<string> filter)
        {
            List<HeapDump.HeapObject> objects = new();
            bool anyErrors = false;
            // Trying several times to dump all candidates
            for (int i = 0; i < 10; i++)
            {
                Logger.Debug($"Trying to dump heap objects. Try #{i + 1}");
                // Clearing leftovers from last trial
                objects.Clear();
                anyErrors = false;

                RefreshRuntime();
                lock (_debugObjectsLock)
                {
                    foreach (ClrObject clrObj in _runtime.Heap.EnumerateObjects())
                    {
                        if (clrObj.IsFree)
                            continue;

                        string objType = clrObj.Type?.Name ?? "Unknown";
                        if (filter(objType))
                        {
                            ulong mt = clrObj.Type.MethodTable;
                            int hashCode = 0;
                            // TODO: Should I make hashcode dumping optional?
                            // maybe make the type filter mandatory?
                            // getting handles to every single object in the heap (in the worst case)
                            // to dump it's hashcode sounds like it'll trigger a GC every single trial...
                            object instance = null;
                            try
                            {
                                instance = _converter.ConvertFromIntPtr(clrObj.Address, mt);
                            }
                            catch (Exception)
                            {
                                // Exiting heap enumeration and signaling that this trial has failed.
                                anyErrors = true;
                                break;
                            }

                            // We got the object in our hands so we haven't spotted a GC collection or anything else scary
                            // now getting the hashcode which is itself a challange since 
                            // objects might (very rudely) throw exceptions on this call.
                            // I'm looking at you, System.Reflection.Emit.SignatureHelper
                            //
                            // We don't REALLY care if we don't get a has code. It just means those objects would
                            // be a bit more hard to grab later.
                            try
                            {
                                hashCode = instance.GetHashCode();
                            }
                            catch
                            {
                                // TODO: Maybe we need a boolean in HeapObject to indicate we couldn't get the hashcode...
                                hashCode = 0;
                            }

                            objects.Add(new HeapDump.HeapObject()
                            {
                                Address = clrObj.Address,
                                MethodTable = clrObj.Type.MethodTable,
                                Type = objType,
                                HashCode = hashCode
                            });
                        }
                    }
                }
                if (!anyErrors)
                {
                    // Success, dumped every instance there is to dump!
                    break;
                }
            }
            if (anyErrors)
            {
                Logger.Debug($"Failt to dump heap objects. Aborting.");
                objects.Clear();
            }
            return (anyErrors, objects);
        }


        private string MakeEventUnsubscribeResponse(HttpListenerRequest arg)
        {
            string tokenStr = arg.QueryString.Get("token");
            if (tokenStr == null || !int.TryParse(tokenStr, out int token))
            {
                return QuickError("Missing parameter 'address'");
            }
            Logger.Debug($"[Diver][MakeEventUnsubscribeResponse] Called! Token: {token}");

            if (_remoteEventHandler.TryGetValue(token, out RegisteredEventHandlerInfo eventInfo))
            {
                eventInfo.EventInfo.RemoveEventHandler(eventInfo.Target, eventInfo.RegisteredProxy);
                _remoteEventHandler.TryRemove(token, out _);
                return "{\"status\":\"OK\"}";
            }
            return QuickError("Unknown token for event callback subscription");
        }
        private string MakeUnhookMethodResponse(HttpListenerRequest arg)
        {
            string tokenStr = arg.QueryString.Get("token");
            if (tokenStr == null || !int.TryParse(tokenStr, out int token))
            {
                return QuickError("Missing parameter 'address'");
            }
            Logger.Debug($"[Diver][MakeUnhookMethodResponse] Called! Token: {token}");

            if (_remoteHooks.TryGetValue(token, out RegisteredMethodHookInfo rmhi))
            {
                ScubaDiver.Hooking.HarmonyWrapper.Instance.RemovePrefix(rmhi.OriginalHookedMethod);
                return "{\"status\":\"OK\"}";
            }
            return QuickError("Unknown token for event callback subscription");
        }
        private string MakeHookMethodResponse(HttpListenerRequest arg)
        {
            Logger.Debug("[Diver] Got Hook Method request!");
            if (!HasCallbackEndpoint)
            {
                return QuickError("Callbacks endpoint missing. You must call /register_callbacks_ep before using this method!");
            }
            string body = null;
            using (StreamReader sr = new(arg.InputStream))
            {
                body = sr.ReadToEnd();
            }

            if (string.IsNullOrEmpty(body))
            {
                return QuickError("Missing body");
            }

            Logger.Debug("[Diver] Parsing Hook Method request body");

            TextReader textReader = new StringReader(body);
            JsonReader jr = new JsonTextReader(textReader);
            JsonSerializer js = new();
            var request = js.Deserialize<FunctionHookRequest>(jr);
            if (request == null)
            {
                return QuickError("Failed to deserialize body");
            }
            Logger.Debug("[Diver] Parsing Hook Method request body -- Done!");

            string typeFullName = request.TypeFullName;
            string methodName = request.MethodName;
            string hookPosition = request.HookPosition;
            HarmonyPatchPosition pos = (HarmonyPatchPosition)Enum.Parse(typeof(HarmonyPatchPosition), hookPosition);
            if (!Enum.IsDefined(typeof(HarmonyPatchPosition), pos))
            {
                return QuickError("hook_position has an invalid or unsupported value");
            }
            Logger.Debug("[Diver] Hook Method = It's pre");

            Type resolvedType = null;
            lock (_debugObjectsLock)
            {
                resolvedType = TypesResolver.Resolve(_runtime, typeFullName);
            }
            if (resolvedType == null)
            {
                return QuickError("Failed to resolve type");
            }
            Logger.Debug("[Diver] Hook Method - Resolved Type");

            Type[] paramTypes = null;
            lock (_debugObjectsLock)
            {
                paramTypes = request.ParametersTypeFullNames.Select(typeFullName => TypesResolver.Resolve(_runtime, typeFullName)).ToArray();
            }
            Console.WriteLine($"[Diver] Hooking - Calling GetMethodRecursive With these params COUNT={paramTypes.Length}");
            Console.WriteLine($"[Diver] Hooking - Calling GetMethodRecursive With these param types: {(string.Join(",", paramTypes.Select(t => t.FullName).ToArray()))}");

            MethodInfo methodInfo = resolvedType.GetMethodRecursive(methodName, paramTypes);
            if (methodInfo == null)
            {
                return QuickError("Failed to find method in type");
            }
            Logger.Debug("[Diver] Hook Method - Resolved Method");

            // We're all good regarding the signature!
            // assign subscriber unique id
            int token = AssignCallbackToken();
            Logger.Debug($"[Diver] Hook Method - Assigned Token: {token}");

            Hooking.HarmonyWrapper.HookCallback patchCallback = (obj, args) => InvokeControllerCallback(token, new object[2] { obj, args });

            Logger.Debug($"[Diver] Hooking function {methodName}...");
            try
            {
                ScubaDiver.Hooking.HarmonyWrapper.Instance.AddHook(methodInfo, pos, patchCallback);
            }
            catch (Exception ex)
            {
                Logger.Debug($"[Diver] Failed to hook func {methodName}. Exception: {ex}");
                return QuickError("Failed insert the hook for the function. HarmonyWrapper.AddHook failed.");
            }
            Logger.Debug($"[Diver] Hooked func {methodName}!");


            // Save all the registeration info so it can be removed later upon request
            _remoteHooks[token] = new RegisteredMethodHookInfo()
            {
                OriginalHookedMethod = methodInfo,
                RegisteredProxy = patchCallback
            };

            EventRegistrationResults erResults = new() { Token = token };
            return JsonConvert.SerializeObject(erResults);
        }


        private string MakeEventSubscribeResponse(HttpListenerRequest arg)
        {
            if (!HasCallbackEndpoint)
            {
                return QuickError("Callbacks endpoint missing. You must call /register_callbacks_ep before using this method!");
            }

            string objAddrStr = arg.QueryString.Get("address");
            if (objAddrStr == null || !ulong.TryParse(objAddrStr, out var objAddr))
            {
                return QuickError("Missing parameter 'address'");
            }
            Logger.Debug($"[Diver][Debug](RegisterEventHandler) objAddrStr={objAddr:X16}");

            // Check if we have this objects in our pinned pool
            if (!TryGetPinnedObject(objAddr, out FrozenObjectInfo foi))
            {
                // Object not pinned, try get it the hard way
                return QuickError("Object at given address wasn't pinned");
            }

            object target = foi.Object;
            Type resolvedType = target.GetType();

            string eventName = arg.QueryString.Get("event");
            if (eventName == null)
            {
                return QuickError("Missing parameter 'event'");
            }
            // TODO: Does this need to be done recursivly?
            EventInfo eventObj = resolvedType.GetEvent(eventName);
            if (eventObj == null)
            {
                return QuickError("Failed to find event in type");
            }

            // Let's make sure the event's delegate type has 2 args - (object, EventArgs or subclass)
            Type eventDelegateType = eventObj.EventHandlerType;
            MethodInfo invokeInfo = eventDelegateType.GetMethod("Invoke");
            ParameterInfo[] paramInfos = invokeInfo.GetParameters();
            if (paramInfos.Length != 2)
            {
                return QuickError("Currently only events with 2 parameters (object & EventArgs) can be subscribed to.");
            }
            // Now I want to make sure the types of the parameters are subclasses of the expected ones.
            // Every type is a subclass of object so I skip the first param
            ParameterInfo secondParamInfo = paramInfos[1];
            Type secondParamType = secondParamInfo.ParameterType;
            if (!secondParamType.IsAssignableFrom(typeof(EventArgs)))
            {
                return QuickError("Second parameter of the event's handler was not a subclass of EventArgs");
            }

            // TODO: Make sure delegate's return type is void? (Who even uses non-void delegates?)

            // We're all good regarding the signature!
            // assign subscriber unique id
            int token = AssignCallbackToken();

            EventHandler eventHandler = (obj, args) => InvokeControllerCallback(token, new object[2] { obj, args });

            Logger.Debug($"[Diver] Adding event handler to event {eventName}...");
            eventObj.AddEventHandler(target, eventHandler);
            Logger.Debug($"[Diver] Added event handler to event {eventName}!");


            // Save all the registeration info so it can be removed later upon request
            _remoteEventHandler[token] = new RegisteredEventHandlerInfo()
            {
                EventInfo = eventObj,
                Target = target,
                RegisteredProxy = eventHandler
            };

            EventRegistrationResults erResults = new() { Token = token };
            return JsonConvert.SerializeObject(erResults);
        }

        private string MakeRegisterCallbacksEndpointResponse(HttpListenerRequest arg)
        {
            // This API is used by the Diver's controller to set an 'End Point' (IP + Port) where
            // it listens to callback requests (via HTTP)
            // Callbacks are used for:
            // 1. Invoking remote events (at Diver's side) callbacks (at controller side)
            // 2. Invoking function hooks (FFU)
            string ipAddrStr = arg.QueryString.Get("ip");
            if (!IPAddress.TryParse(ipAddrStr, out IPAddress ipa))
            {
                return QuickError("Parameter 'ip' couldn't be parsed to a valid IP Address");
            }
            string portAddrStr = arg.QueryString.Get("port");
            if (!int.TryParse(portAddrStr, out int port))
            {
                return QuickError("Parameter 'port' couldn't be parsed to a valid IP Address");
            }
            _callbacksEndpoint = new IPEndPoint(ipa, port);
            Logger.Debug($"[Diver] Register Callback Endpoint complete. Endpoint: {_callbacksEndpoint}");
            return "{\"status\":\"OK\"}";
        }
        private string MakeUnpinResponse(HttpListenerRequest arg)
        {
            string objAddrStr = arg.QueryString.Get("address");
            if (objAddrStr == null || !ulong.TryParse(objAddrStr, out var objAddr))
            {
                return QuickError("Missing parameter 'address'");
            }
            Logger.Debug($"[Diver][Debug](UnpinObject) objAddrStr={objAddr:X16}\n");

            // Check if we have this objects in our pinned pool
            if (TryGetPinnedObject(objAddr, out _))
            {
                // Found pinned object!
                UnpinObject(objAddr);
                return "{\"status\":\"OK\"}";
            }
            else
            {
                // Object not pinned, try get it the hard way
                return QuickError("Object at given address wasn't pinned");
            }
        }
        private string MakeCreateObjectResponse(HttpListenerRequest arg)
        {
            Logger.Debug("[Diver] Got /create_object request!");
            string body = null;
            using (StreamReader sr = new(arg.InputStream))
            {
                body = sr.ReadToEnd();
            }

            if (string.IsNullOrEmpty(body))
            {
                return QuickError("Missing body");
            }

            TextReader textReader = new StringReader(body);
            JsonReader jr = new JsonTextReader(textReader);
            JsonSerializer js = new();
            var request = js.Deserialize<CtorInvocationRequest>(jr);
            if (request == null)
            {
                return QuickError("Failed to deserialize body");
            }


            Type t = null;
            lock (_debugObjectsLock)
            {
                t = TypesResolver.Resolve(_runtime, request.TypeFullName);
            }
            if (t == null)
            {
                return QuickError("Failed to resolve type");
            }

            List<object> paramsList = new();
            if (request.Parameters.Any())
            {
                Logger.Debug($"[Diver] Ctor'ing with parameters. Count: {request.Parameters.Count}");
                paramsList = request.Parameters.Select(ParseParameterObject).ToList();
            }
            else
            {
                // No parameters.
                Logger.Debug("[Diver] Ctor'ing without parameters");
            }

            object createdObject = null;
            try
            {
                object[] paramsArray = paramsList.ToArray();
                createdObject = Activator.CreateInstance(t, paramsArray);
            }
            catch
            {
                Debugger.Launch();
                return QuickError("Activator.CreateInstance threw an exception");
            }

            if (createdObject == null)
            {
                return QuickError("Activator.CreateInstance returned null");
            }

            // Need to return the results. If it's primitive we'll encode it
            // If it's non-primitive we pin it and send the address.
            ObjectOrRemoteAddress res;
            ulong pinAddr;
            if (createdObject.GetType().IsPrimitiveEtc())
            {
                // TODO: Something else?
                pinAddr = 0xeeffeeff;
                res = ObjectOrRemoteAddress.FromObj(createdObject);
            }
            else
            {
                // Pinning results
                var pinnedObj = PinObject(createdObject);
                pinAddr = pinnedObj.Address;
                res = ObjectOrRemoteAddress.FromToken(pinAddr, createdObject.GetType().FullName);
            }


            InvocationResults invoRes = new()
            {
                ReturnedObjectOrAddress = res,
                VoidReturnType = false
            };
            return JsonConvert.SerializeObject(invoRes);

        }
        private object ParseParameterObject(ObjectOrRemoteAddress param)
        {
            switch (param)
            {
                case { IsNull: true }:
                    return null;
                case { IsRemoteAddress: false }:
                    return PrimitivesEncoder.Decode(param.EncodedObject, param.Type);
                case { IsRemoteAddress: true }:
                    if (TryGetPinnedObject(param.RemoteAddress, out FrozenObjectInfo poi))
                    {
                        return poi.Object;
                    }
                    break;
            }

            Debugger.Launch();
            throw new NotImplementedException(
                $"Don't know how to parse this parameter into an object of type `{param.Type}`");
        }
        private string MakeInvokeResponse(HttpListenerRequest arg)
        {
            Logger.Debug("[Diver] Got /Invoke request!");
            string body = null;
            using (StreamReader sr = new(arg.InputStream))
            {
                body = sr.ReadToEnd();
            }

            if (string.IsNullOrEmpty(body))
            {
                return QuickError("Missing body");
            }

            TextReader textReader = new StringReader(body);
            JsonReader jr = new JsonTextReader(textReader);
            JsonSerializer js = new();
            var request = js.Deserialize<InvocationRequest>(jr);
            if (request == null)
            {
                return QuickError("Failed to deserialize body");
            }

            // Need to figure target instance and the target type.
            // In case of a static call the target instance stays null.
            object instance = null;
            Type dumpedObjType;
            if (request.ObjAddress == 0)
            {
                //
                // Null target - static call
                //

                lock (_debugObjectsLock)
                {
                    dumpedObjType = TypesResolver.Resolve(_runtime, request.TypeFullName);
                }
            }
            else
            {
                //
                // Non-null target object address. Non-static call
                //

                // Check if we have this objects in our pinned pool
                if (TryGetPinnedObject(request.ObjAddress, out FrozenObjectInfo poi))
                {
                    // Found pinned object!
                    instance = poi.Object;
                    dumpedObjType = instance.GetType();
                }
                else
                {
                    // Object not pinned, try get it the hard way
                    ClrObject clrObj;
                    lock (_debugObjectsLock)
                    {
                        clrObj = _runtime.Heap.GetObject(request.ObjAddress);
                    }
                    if (clrObj.Type == null)
                    {
                        return QuickError("'address' points at an invalid address");
                    }

                    // Make sure it's still in place
                    RefreshRuntime();
                    lock (_debugObjectsLock)
                    {
                        clrObj = _runtime.Heap.GetObject(request.ObjAddress);
                    }
                    if (clrObj.Type == null)
                    {
                        return
                            QuickError("Object moved since last refresh. 'address' now points at an invalid address.");
                    }

                    ulong mt = clrObj.Type.MethodTable;
                    dumpedObjType = clrObj.Type.GetRealType();
                    try
                    {
                        instance = _converter.ConvertFromIntPtr(clrObj.Address, mt);
                    }
                    catch (Exception)
                    {
                        return
                            QuickError("Couldn't get handle to requested object. It could be because the Method Table mismatched or a GC collection happened.");
                    }
                }
            }


            //
            // We have our target and it's type. No look for a matching overload for the
            // function to invoke.
            //
            List<object> paramsList = new();
            if (request.Parameters.Any())
            {
                Logger.Debug($"[Diver] Invoking with parameters. Count: {request.Parameters.Count}");
                paramsList = request.Parameters.Select(ParseParameterObject).ToList();
            }
            else
            {
                // No parameters.
                Logger.Debug("[Diver] Invoking without parameters");
            }

            Logger.Debug($"[Diver] Resolved target object type: {dumpedObjType.FullName}");
            // Infer parameter types from received parameters.
            // Note that for 'null' arguments we don't know the type so we use a "Wild Card" type
            Type[] argumentTypes = paramsList.Select(p => p?.GetType() ?? new WildCardType()).ToArray();

            // Search the method with the matching signature
            var method = dumpedObjType.GetMethodRecursive(request.MethodName, argumentTypes);
            if (method == null)
            {
                Debugger.Launch();
                Logger.Debug($"[Diver] Failed to Resolved method :/");
                return QuickError("Couldn't find method in type.");
            }

            string argsSummary = string.Join(", ", argumentTypes.Select(arg => arg.Name));
            Logger.Debug($"[Diver] Resolved method: {method.Name}({argsSummary}), Containing Type: {method.DeclaringType}");

            object results = null;
            try
            {
                argsSummary = string.Join(", ", paramsList.Select(param => param?.ToString() ?? "null"));
                if (string.IsNullOrEmpty(argsSummary))
                    argsSummary = "No Arguments";
                Logger.Debug($"[Diver] Invoking {method.Name} with those args (Count: {paramsList.Count}): `{argsSummary}`");
                results = method.Invoke(instance, paramsList.ToArray());
                Console.WriteLine("[Diver] invoked function finished!");
            }
            catch (Exception e)
            {
                return $"{{\"error\":\"Invocation caused exception: {e}\"}}";
            }

            InvocationResults invocResults;
            if (method.ReturnType == typeof(void))
            {
                // Not expecting results.
                invocResults = new InvocationResults() { VoidReturnType = true };
            }
            else
            {
                if (results == null)
                {
                    // Got back a null...
                    invocResults = new InvocationResults()
                    {
                        VoidReturnType = false,
                        ReturnedObjectOrAddress = ObjectOrRemoteAddress.Null
                    };
                }
                else
                {
                    // Need to return the results. If it's primitive we'll encode it
                    // If it's non-primitive we pin it and send the address.
                    ObjectOrRemoteAddress returnValue;
                    if (results.GetType().IsPrimitiveEtc())
                    {
                        returnValue = ObjectOrRemoteAddress.FromObj(results);
                    }
                    else
                    {
                        // Pinning results
                        FrozenObjectInfo foi = PinObject(results);
                        ulong resultsAddress = foi.Address;
                        Type resultsType = results.GetType();
                        returnValue = ObjectOrRemoteAddress.FromToken(resultsAddress, resultsType.Name);
                    }


                    invocResults = new InvocationResults()
                    {
                        VoidReturnType = false,
                        ReturnedObjectOrAddress = returnValue
                    };
                }
            }
            return JsonConvert.SerializeObject(invocResults);
        }
        private string MakeGetFieldResponse(HttpListenerRequest arg)
        {
            Logger.Debug("[Diver] Got /get_field request!");
            string body = null;
            using (StreamReader sr = new(arg.InputStream))
            {
                body = sr.ReadToEnd();
            }

            if (string.IsNullOrEmpty(body))
            {
                return QuickError("Missing body");
            }

            TextReader textReader = new StringReader(body);
            JsonReader jr = new JsonTextReader(textReader);
            JsonSerializer js = new();
            FieldSetRequest request = js.Deserialize<FieldSetRequest>(jr);
            if (request == null)
            {
                return QuickError("Failed to deserialize body");
            }

            // Need to figure target instance and the target type.
            // In case of a static call the target instance stays null.
            Type dumpedObjType;
            object results;
            if (request.ObjAddress == 0)
            {
                // Null Target -- Getting a Static field
                lock (_debugObjectsLock)
                {
                    dumpedObjType = TypesResolver.Resolve(_runtime, request.TypeFullName);
                }
                FieldInfo staticFieldInfo = dumpedObjType.GetField(request.FieldName);
                if (!staticFieldInfo.IsStatic)
                {
                    return QuickError("Trying to get field with a null target bu the field was not a static one");
                }

                results = staticFieldInfo.GetValue(null);
            }
            else
            {
                object instance;
                // Check if we have this objects in our pinned pool
                if (TryGetPinnedObject(request.ObjAddress, out FrozenObjectInfo poi))
                {
                    // Found pinned object!
                    instance = poi.Object;
                    dumpedObjType = instance.GetType();
                }
                else
                {
                    return QuickError("Can't get field of a unpinned objects");
                }

                // Search the method with the matching signature
                var fieldInfo = dumpedObjType.GetFieldRecursive(request.FieldName);
                if (fieldInfo == null)
                {
                    Debugger.Launch();
                    Logger.Debug($"[Diver] Failed to Resolved field :/");
                    return QuickError("Couldn't find field in type.");
                }

                Logger.Debug($"[Diver] Resolved field: {fieldInfo.Name}, Containing Type: {fieldInfo.DeclaringType}");

                try
                {
                    results = fieldInfo.GetValue(instance);
                }
                catch (Exception e)
                {
                    return $"{{\"error\":\"Invocation caused exception: {e}\"}}";
                }
            }


            // Return the value we just set to the field to the caller...
            InvocationResults invocResults;
            {
                ObjectOrRemoteAddress returnValue;
                if (results.GetType().IsPrimitiveEtc())
                {
                    returnValue = ObjectOrRemoteAddress.FromObj(results);
                }
                else
                {
                    // Pinning results
                    FrozenObjectInfo resultsFoi = PinObject(results);
                    ulong resultsAddress = resultsFoi.Address;
                    Type resultsType = results.GetType();
                    returnValue = ObjectOrRemoteAddress.FromToken(resultsAddress, resultsType.Name);
                }

                invocResults = new InvocationResults()
                {
                    VoidReturnType = false,
                    ReturnedObjectOrAddress = returnValue
                };
            }
            return JsonConvert.SerializeObject(invocResults);

        }
        private string MakeSetFieldResponse(HttpListenerRequest arg)
        {
            Logger.Debug("[Diver] Got /set_field request!");
            string body = null;
            using (StreamReader sr = new(arg.InputStream))
            {
                body = sr.ReadToEnd();
            }

            if (string.IsNullOrEmpty(body))
            {
                return QuickError("Missing body");
            }

            TextReader textReader = new StringReader(body);
            JsonReader jr = new JsonTextReader(textReader);
            JsonSerializer js = new();
            var request = js.Deserialize<FieldSetRequest>(jr);
            if (request == null)
            {
                return QuickError("Failed to deserialize body");
            }

            Type dumpedObjType;
            if (request.ObjAddress == 0)
            {
                return QuickError("Can't set field of a null target");
            }


            // Need to figure target instance and the target type.
            // In case of a static call the target instance stays null.
            object instance;
            // Check if we have this objects in our pinned pool
            if (TryGetPinnedObject(request.ObjAddress, out FrozenObjectInfo poi))
            {
                // Found pinned object!
                instance = poi.Object;
                dumpedObjType = instance.GetType();
            }
            else
            {
                // Object not pinned, try get it the hard way
                ClrObject clrObj = default(ClrObject);
                lock (_debugObjectsLock)
                {
                    clrObj = _runtime.Heap.GetObject(request.ObjAddress);
                    if (clrObj.Type == null)
                    {
                        return QuickError("'address' points at an invalid address");
                    }

                    // Make sure it's still in place
                    RefreshRuntime();
                    clrObj = _runtime.Heap.GetObject(request.ObjAddress);
                }
                if (clrObj.Type == null)
                {
                    return
                        QuickError("Object moved since last refresh. 'address' now points at an invalid address.");
                }

                ulong mt = clrObj.Type.MethodTable;
                dumpedObjType = clrObj.Type.GetRealType();
                try
                {
                    instance = _converter.ConvertFromIntPtr(clrObj.Address, mt);
                }
                catch (Exception)
                {
                    return
                        QuickError("Couldn't get handle to requested object. It could be because the Method Table or a GC collection happened.");
                }
            }

            // Search the method with the matching signature
            var fieldInfo = dumpedObjType.GetFieldRecursive(request.FieldName);
            if (fieldInfo == null)
            {
                Debugger.Launch();
                Logger.Debug($"[Diver] Failed to Resolved field :/");
                return QuickError("Couldn't find field in type.");
            }
            Logger.Debug($"[Diver] Resolved field: {fieldInfo.Name}, Containing Type: {fieldInfo.DeclaringType}");

            object results = null;
            try
            {
                object value = ParseParameterObject(request.Value);
                fieldInfo.SetValue(instance, value);
                // Reading back value to return to caller. This is expected C# behaviour:
                // int x = this.field_y = 3; // Makes both x and field_y equal 3.
                results = fieldInfo.GetValue(instance);
            }
            catch (Exception e)
            {
                return $"{{\"error\":\"Invocation caused exception: {e}\"}}";
            }


            // Return the value we just set to the field to the caller...
            InvocationResults invocResults;
            {
                ObjectOrRemoteAddress returnValue;
                if (results.GetType().IsPrimitiveEtc())
                {
                    returnValue = ObjectOrRemoteAddress.FromObj(results);
                }
                else
                {
                    // Pinning results
                    FrozenObjectInfo resultsFoi = PinObject(results);
                    ulong resultsAddress = resultsFoi.Address;
                    Type resultsType = results.GetType();
                    returnValue = ObjectOrRemoteAddress.FromToken(resultsAddress, resultsType.Name);
                }

                invocResults = new InvocationResults()
                {
                    VoidReturnType = false,
                    ReturnedObjectOrAddress = returnValue
                };
            }
            return JsonConvert.SerializeObject(invocResults);
        }
        private string MakeArrayItemResponse(HttpListenerRequest arg)
        {
            string objAddrStr = arg.QueryString.Get("address");
            string indexStr = arg.QueryString.Get("index");
            bool pinningRequested = arg.QueryString.Get("pinRequest").ToUpper() == "TRUE";
            if (!ulong.TryParse(objAddrStr, out var objAddr))
            {
                return QuickError("Parameter 'address' could not be parsed as ulong");
            }
            if (!int.TryParse(indexStr, out var index))
            {
                return QuickError("Parameter 'index' could not be parsed as ulong");
            }

            // Check if we have this objects in our pinned pool
            if (!TryGetPinnedObject(objAddr, out FrozenObjectInfo arrayFoi))
            {
                // Object not pinned, try get it the hard way
                return QuickError("Object at given address wasn't pinned");
            }

            IList asList = (arrayFoi.Object as IList);
            object[] asArray = asList?.Cast<object>().ToArray();
            if (asArray == null)
            {
                return QuickError("Object at given address wasn't an IList");
            }
            int length = asArray.Length;


            if (index >= length)
            {
                return QuickError("Index out of range");
            }

            // Get the item
            object item = asArray[index];

            ObjectOrRemoteAddress res;
            ulong pinAddr;
            if (item.GetType().IsPrimitiveEtc())
            {
                // TODO: Something else?
                res = ObjectOrRemoteAddress.FromObj(item);
            }
            else
            {
                // Non-primitive results must be pinned before returning their remote address
                // TODO: If a RemoteObject is not created for this object later and the item is not automaticlly unfreezed it might leak.
                if (IsPinned(item, out FrozenObjectInfo itemFoi))
                {
                    // Sanity: Make sure the pinned item is the same as the one we are looking for
                    if (itemFoi.Object != item)
                    {
                        return $"{{\"error\":\"An object was pinned at that address but it's {nameof(FrozenObjectInfo)} " +
                            $"object pointer at a different object\"}}";
                    }
                }
                else
                {
                    // Item not pinned yet, let's do it.
                    itemFoi = PinObject(item);
                }

                res = ObjectOrRemoteAddress.FromToken(itemFoi.Address, item.GetType().FullName);
            }


            InvocationResults invokeRes = new()
            {
                VoidReturnType = false,
                ReturnedObjectOrAddress = res
            };


            return JsonConvert.SerializeObject(invokeRes);
        }

        private string MakeObjectResponse(HttpListenerRequest arg)
        {
            string objAddrStr = arg.QueryString.Get("address");
            bool pinningRequested = arg.QueryString.Get("pinRequest").ToUpper() == "TRUE";
            bool hashCodeFallback = arg.QueryString.Get("hashcode_fallback").ToUpper() == "TRUE";
            string hashCodeStr = arg.QueryString.Get("hashcode");
            int userHashcode = 0;
            if (objAddrStr == null)
            {
                return QuickError("Missing parameter 'address'");
            }
            if (!ulong.TryParse(objAddrStr, out var objAddr))
            {
                return QuickError("Parameter 'address' could not be parsed as ulong");
            }
            if (hashCodeFallback)
            {
                if (!int.TryParse(hashCodeStr, out userHashcode))
                {
                    return QuickError("Parameter 'hashcode_fallback' was 'true' but the hashcode argument was missing or not an int");
                }
            }

            // Check if we have this objects in our pinned pool
            if (TryGetPinnedObject(objAddr, out FrozenObjectInfo foi))
            {
                // Found pinned object!
                object pinnedInstance = foi.Object;
                ObjectDump alreadyPinnedObjDump = ObjectDumpFactory.Create(pinnedInstance, objAddr, foi.Address);
                return JsonConvert.SerializeObject(alreadyPinnedObjDump);
            }

            // Object not pinned, try get it the hard way
            // Make sure we had such an object in the last dumped runtime (this will help us later if the object moves
            // since we'll know what type we are looking for)
            // Make sure it's still in place
            ClrObject lastKnownClrObj = _runtime.Heap.GetObject(objAddr);
            if (lastKnownClrObj == null)
            {
                return QuickError("No object in this address. Try finding it's address again and dumping again.");
            }

            // Make sure it's still in place by refreshing the runtime
            RefreshRuntime();
            ClrObject clrObj = default(ClrObject);
            lock (_debugObjectsLock)
            {
                clrObj = _runtime.Heap.GetObject(objAddr);
            }

            //
            // Figuring out the Method Table value and the actual Object's address
            //
            ulong methodTable;
            ulong finalObjAddress;
            if (clrObj.Type != null)
            {
                methodTable = clrObj.Type.MethodTable;
                finalObjAddress = clrObj.Address;
            }
            else
            {
                // Object moved! 
                // Let's try and save the day with some hashcode filtering (if user allowed us)
                if (!hashCodeFallback)
                {
                    return
                        "{\"error\":\"Object moved since last refresh. 'address' now points at an invalid address. " +
                        "Hash Code fallback was NOT activated\"}";
                }

                Predicate<string> typeFilter = (string type) => type.Contains(lastKnownClrObj.Type.Name);
                (bool anyErrors, List<HeapDump.HeapObject> objects) = GetHeapObjects(typeFilter);
                if (anyErrors)
                {
                    return
                        "{\"error\":\"Object moved since last refresh. 'address' now points at an invalid address. " +
                        "Hash Code fallback was activated but dumping function failed so non hash codes were checked\"}";
                }
                var matches = objects.Where(heapObj => heapObj.HashCode == userHashcode).ToList();
                if (matches.Count != 1)
                {
                    return
                        "{\"error\":\"Object moved since last refresh. 'address' now points at an invalid address. " +
                        $"Hash Code fallback was activated but {((matches.Count > 1) ? "too many (>1)" : "no")} objects with the same hash code were found\"}}";
                }

                // Single match! We are as lucky as it gets :)
                HeapDump.HeapObject heapObj = matches.Single();
                ulong newObjAddress = heapObj.Address;
                finalObjAddress = newObjAddress;
                methodTable = heapObj.MethodTable;
            }

            //
            // Actually convert the address back into an Object reference.
            //
            object instance;
            try
            {
                instance = _converter.ConvertFromIntPtr(finalObjAddress, methodTable);
            }
            catch (ArgumentException)
            {
                return QuickError("Method Table value mismatched");
            }

            // Pin the result object if requested
            ulong pinnedAddress = 0;
            if (pinningRequested)
            {
                foi = PinObject(instance);
                pinnedAddress = foi.Address;
            }

            ObjectDump od = ObjectDumpFactory.Create(instance, objAddr, pinnedAddress);
            return JsonConvert.SerializeObject(od);
        }

        private string MakeDieResponse(HttpListenerRequest req)
        {
            Logger.Debug("[Diver] Die command received");
            bool forceKill = req.QueryString.Get("force")?.ToUpper() == "TRUE";
            lock (_registeredPidsLock)
            {
                if (_registeredPids.Count > 0 && !forceKill)
                {
                    Logger.Debug("[Diver] Die command failed - More clients exist.");
                    return "{\"status\":\"Error more clients remaining. You can use the force=true argument to ignore this check.\"}";
                }
            }

            Logger.Debug("[Diver] Die command accepted.");
            _stayAlive.Reset();
            return "{\"status\":\"Goodbye\"}";
        }
        private string MakeTypesResponse(HttpListenerRequest req)
        {
            string assembly = req.QueryString.Get("assembly");

            // Try exact match assembly 
            IEnumerable<ClrModule> allAssembliesInApp = null;
            lock (_debugObjectsLock)
            {
                allAssembliesInApp = _runtime.AppDomains.SelectMany(appDom => appDom.Modules);
            }
            List<ClrModule> matchingAssemblies = allAssembliesInApp.Where(module => Path.GetFileNameWithoutExtension(module.Name) == assembly).ToList();
            if (matchingAssemblies.Count == 0)
            {
                // No exact matches, widen search to any assembly *containing* the query
                matchingAssemblies = allAssembliesInApp.Where(module => Path.GetFileNameWithoutExtension(module.Name).Contains(assembly)).ToList();
            }

            if (!matchingAssemblies.Any())
            {
                // No matching assemblies found
                return QuickError("No assemblies found matching the query");
            }
            else if (matchingAssemblies.Count > 1)
            {
                return $"{{\"error\":\"Too many assemblies found matching the query. Expected: 1, Got: {matchingAssemblies.Count}\"}}";
            }

            // Got here - we have a single matching assembly.
            ClrModule matchingAssembly = matchingAssemblies.Single();

            var typeNames = from tuple in matchingAssembly.OldSchoolEnumerateTypeDefToMethodTableMap()
                            let token = tuple.Token
                            let typeName = matchingAssembly.ResolveToken(token)?.Name ?? "Unknown"
                            select new TypesDump.TypeIdentifiers()
                            { MethodTable = tuple.MethodTable, Token = token, TypeName = typeName };


            TypesDump dump = new()
            {
                AssemblyName = assembly,
                Types = typeNames.ToList()
            };

            return JsonConvert.SerializeObject(dump);
        }
        private string MakeHeapResponse(HttpListenerRequest httpReq)
        {
            string filter = httpReq.QueryString.Get("type_filter");

            // Default filter - no filter. Just return everything.
            Predicate<string> matchesFilter = (typeName) => true;
            if (filter != null)
            {
                string noStartsFilter = filter.Trim('*');
                // User specified a filter. Looking for wild cards
                if (filter.StartsWith("*"))
                {
                    if (filter.EndsWith("*"))
                    {
                        // Filter of format "*phrase*", looking anywhere inside the type name
                        matchesFilter = (typeName) => typeName.Contains(noStartsFilter);
                    }
                    else
                    {
                        // Filter of format "*phrase", looking for specific suffix
                        matchesFilter = (typeName) => typeName.EndsWith(noStartsFilter);
                    }
                }
                else
                {
                    if (filter.EndsWith("*"))
                    {
                        // Filter of format "phrase*", looking for specific prefix
                        matchesFilter = (typeName) => typeName.StartsWith(noStartsFilter);
                    }
                    else
                    {
                        // Filter has no wildcards - looking for specific type
                        matchesFilter = (typeName) => typeName == filter;
                    }
                }
            }

            (bool anyErrors, List<HeapDump.HeapObject> objects) = GetHeapObjects(matchesFilter);
            if (anyErrors)
            {
                return "{\"error\":\"All dumping trials failed because at least 1 " +
                    "object moved between the snapshot and the heap enumeration\"}";
            }

            HeapDump hd = new() { Objects = objects };

            var resJson = JsonConvert.SerializeObject(hd);
            return resJson;
        }
        private string MakeDomainsResponse(HttpListenerRequest req)
        {
            // TODO: Allow moving between domains?
            List<DomainsDump.AvailableDomain> available = new();
            lock (_debugObjectsLock)
            {
                foreach (ClrAppDomain clrAppDomain in _runtime.AppDomains)
                {
                    available.Add(new DomainsDump.AvailableDomain()
                    {
                        Name = clrAppDomain.Name,
                        AvailableModules = clrAppDomain.Modules.Select(m => Path.GetFileNameWithoutExtension(m.Name)).ToList()
                    });
                }
            }

            DomainsDump dd = new()
            {
                Current = AppDomain.CurrentDomain.FriendlyName,
                AvailableDomains = available
            };

            return JsonConvert.SerializeObject(dd);
        }
        private string MakeTypeResponse(HttpListenerRequest req) => MakeTypeResponse(req.QueryString);
        public string MakeTypeResponse(NameValueCollection queryString)
        {
            string type = queryString.Get("name");
            if (string.IsNullOrEmpty(type))
            {
                return QuickError("Missing parameter 'name'");
            }

            string assembly = queryString.Get("assembly");
            Type resolvedType = null;
            lock (_debugObjectsLock)
            {
                resolvedType = TypesResolver.Resolve(_runtime, type, assembly);
            }

            // 
            // Defining a sub-function that parses a type and it's parents recursively
            //
            TypeDump ParseType(Type typeObj)
            {
                if (typeObj == null) return null;

                var ctors = typeObj.GetConstructors((BindingFlags)0xffff).Select(ci => new TypeDump.TypeMethod(ci))
                    .ToList();
                var methods = typeObj.GetMethods((BindingFlags)0xffff).Select(mi => new TypeDump.TypeMethod(mi))
                    .ToList();
                var fields = typeObj.GetFields((BindingFlags)0xffff).Select(fi => new TypeDump.TypeField(fi))
                    .ToList();
                var events = typeObj.GetEvents((BindingFlags)0xffff).Select(ei => new TypeDump.TypeEvent(ei))
                    .ToList();
                var props = typeObj.GetProperties((BindingFlags)0xffff).Select(pi => new TypeDump.TypeProperty(pi))
                    .ToList();

                TypeDump td = new()
                {
                    Type = typeObj.FullName,
                    Assembly = typeObj.Assembly.GetName().Name,
                    Methods = methods,
                    Constructors = ctors,
                    Fields = fields,
                    Events = events,
                    Properties = props,
                    IsArray = typeObj.IsArray,
                };
                if (typeObj != typeof(object))
                {
                    // Has parent. Parse it as well
                    td.ParentDump = ParseType(typeObj.BaseType);
                }

                return td;
            }

            if (resolvedType != null)
            {
                TypeDump recusiveTypeDump = ParseType(resolvedType);
                return JsonConvert.SerializeObject(recusiveTypeDump);
            }

            return QuickError("Failed to find type in searched assemblies");
        }

        public string QuickError(string error)
        {
            DiverError errResults = new(error);
            return JsonConvert.SerializeObject(errResults);
        }


        // IDisposable
        public void Dispose()
        {
            lock (_debugObjectsLock)
            {
                _runtime?.Dispose();
                _dt?.Dispose();
            }
        }

    }
}