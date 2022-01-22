using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Reflection;
using System.Runtime;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
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
        private object _debugObjectsLock = new object();
        private DataTarget _dt = null;
        private ClrRuntime _runtime = null;
        private Converter<object> _converter = new Converter<object>();

        // HTTP Responses fields
        private Dictionary<string, Func<HttpListenerRequest, string>> _responseBodyCreators;

        // Pinning objects fields
        private ConcurrentDictionary<ulong, FrozenObjectInfo> _pinnedObjects;

        // Callbacks Endpoint of the Controller process
        IPEndPoint _callbacksEndpoint;
        int _nextAvilableCallbackToken = 0;
        private ConcurrentDictionary<int, IProxyFunctionHolder> _tokensToRegisteredEventHandlers;

        private ManualResetEvent _stayAlive = new ManualResetEvent(true);

        public bool HasCallbackEndpoint => _callbacksEndpoint != null;

        public Diver()
        {
            _responseBodyCreators = new Dictionary<string, Func<HttpListenerRequest, string>>()
            {
                {"/die", MakeDieResponse},
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
            _tokensToRegisteredEventHandlers = new ConcurrentDictionary<int, IProxyFunctionHolder>();
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
            HttpListener listener = new HttpListener();
            string listeningUrl = $"http://127.0.0.1:{listenPort}/";
            listener.Prefixes.Add(listeningUrl);
            listener.Start();
            Logger.Debug($"[Diver] Listening on {listeningUrl}...");

            Dispatcher(listener);

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
                body = respBodyGenerator(request);
            }
            else
            {
                body = "{\"error\":\"Unknown Command\"}";
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
            while (_stayAlive.WaitOne())
            {
                HttpListenerContext requestContext = listener.GetContext();
                Task.Run(() =>
                {
                    Console.WriteLine($"[Diver][TASK ID {Thread.CurrentThread.ManagedThreadId}] New Dispatched Request STARTED");
                    HandleDispatchedRequest(requestContext);
                    Console.WriteLine($"[Diver][TASK ID {Thread.CurrentThread.ManagedThreadId}] New Dispatched Request FINISHED");
                });
            }

            Logger.Debug("[Diver] HTTP Loop ended. Cleaning up");

            Logger.Debug("[Diver] Removing all event subscriptions");
            foreach (int token in _tokensToRegisteredEventHandlers.Keys.ToList())
            {
                IProxyFunctionHolder proxyHolder = _tokensToRegisteredEventHandlers[token];
                if (proxyHolder is RegisteredEventHandlerInfo rehi)
                {
                    rehi.EventInfo.RemoveEventHandler(rehi.Target, rehi.RegisteredProxy);
                    _tokensToRegisteredEventHandlers.TryRemove(token, out _);
                }
                else if (proxyHolder is RegisteredMethodHookInfo rmhi)
                {
                    ScubaDiver.Hooking.HarmonyWrapper.Instance.RemovePrefix(rmhi.OriginalHookedMethod);
                }
            }
            Logger.Debug("[Diver] Removed all event subscriptions");
        }


        public int AssignCallbackToken() => Interlocked.Increment(ref _nextAvilableCallbackToken);
        public void InvokeControllerCallback(int token, params object[] parameters)
        {
            ReverseCommunicator reverseCommunicator = new ReverseCommunicator(_callbacksEndpoint);

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
                    FrozenObjectInfo foi;
                    if (!IsPinned(parameter, out foi))
                    {
                        // Pin and mark for unpinning later
                        foi = PinObject(parameter);
                    }

                    remoteParams[i] = ObjectOrRemoteAddress.FromToken(foi.Address, foi.Object.GetType().FullName);
                }
            }

            // Call callback at controller
            Logger.Debug("[InvokeControllerCallback] Before reverseCommunicator.InvokeCallback");
            reverseCommunicator.InvokeCallback(token, remoteParams);
            Logger.Debug("[InvokeControllerCallback] After reverseCommunicator.InvokeCallback");
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
                    foi.FreezeTask.Wait();
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
            List<HeapDump.HeapObject> objects = new List<HeapDump.HeapObject>();
            bool anyErrors = false;
            // Trying several times to dump all candidates
            for (int i = 0; i < 3; i++)
            {
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
                objects.Clear();
            }
            return (anyErrors, objects);
        }


        private string MakeEventUnsubscribeResponse(HttpListenerRequest arg)
        {
            string tokenStr = arg.QueryString.Get("token");
            if (tokenStr == null || !int.TryParse(tokenStr, out int token))
            {
                return "{\"error\":\"Missing parameter 'address'\"}";
            }
            Logger.Debug($"[Diver][MakeEventUnsubscribeResponse] Called! Token: {token}");

            if (_tokensToRegisteredEventHandlers.TryGetValue(token, out IProxyFunctionHolder proxyHolder))
            {
                if (proxyHolder is RegisteredEventHandlerInfo eventInfo)
                {
                    eventInfo.EventInfo.RemoveEventHandler(eventInfo.Target, eventInfo.RegisteredProxy);
                    _tokensToRegisteredEventHandlers.TryRemove(token, out _);
                    return "{\"status\":\"OK\"}";
                }
                else
                {
                    return "{\"error\":\"A proxy holder object was found for the given token but it was not a RegisteredEventHandlerInfo object\"}";
                }
            }
            return "{\"error\":\"Unknown token for event callback subscription\"}";
        }
        private string MakeUnhookMethodResponse(HttpListenerRequest arg)
        {
            string tokenStr = arg.QueryString.Get("token");
            if (tokenStr == null || !int.TryParse(tokenStr, out int token))
            {
                return "{\"error\":\"Missing parameter 'address'\"}";
            }
            Logger.Debug($"[Diver][MakeUnhookMethodResponse] Called! Token: {token}");

            if (_tokensToRegisteredEventHandlers.TryGetValue(token, out IProxyFunctionHolder proxyHolder))
            {
                if (proxyHolder is RegisteredMethodHookInfo rmhi)
                {
                    ScubaDiver.Hooking.HarmonyWrapper.Instance.RemovePrefix(rmhi.OriginalHookedMethod);
                    return "{\"status\":\"OK\"}";
                }
                else
                {
                    return "{\"error\":\"A proxy holder object was found for the given token but it was not a RegisteredEventHandlerInfo object\"}";
                }
            }
            return "{\"error\":\"Unknown token for event callback subscription\"}";
        }
        private string MakeHookMethodResponse(HttpListenerRequest arg)
        {
            Logger.Debug("[Diver] Got Hook Method request!");
            if (!HasCallbackEndpoint)
            {
                return "{\"error\":\"Callbacks endpoint missing. You must call /register_callbacks_ep before using this method!\"}";
            }
            string body = null;
            using (StreamReader sr = new StreamReader(arg.InputStream))
            {
                body = sr.ReadToEnd();
            }

            if (string.IsNullOrEmpty(body))
            {
                return "{\"error\":\"Missing body\"}";
            }

            Logger.Debug("[Diver] Parsing Hook Method request body");

            TextReader textReader = new StringReader(body);
            JsonReader jr = new JsonTextReader(textReader);
            JsonSerializer js = new JsonSerializer();
            var request = js.Deserialize<FunctionHookRequest>(jr);
            if (request == null)
            {
                return "{\"error\":\"Failed to deserialize body\"}";
            }
            Logger.Debug("[Diver] Parsing Hook Method request body -- Done!");

            string typeFullName = request.TypeFullName;
            string methodName = request.MethodName;
            string hookPosition = request.HookPosition;
            HarmonyPatchPosition pos = (HarmonyPatchPosition)Enum.Parse(typeof(HarmonyPatchPosition), hookPosition);
            if (!Enum.IsDefined(typeof(HarmonyPatchPosition), pos))
            {
                return "{\"error\":\"hook_position has an invalid or unsupported value\"}";
            }
            Logger.Debug("[Diver] Hook Method = It's pre");

            Type resolvedType = null;
            lock (_debugObjectsLock)
            {
                resolvedType = TypesResolver.Resolve(_runtime, typeFullName);
            }
            if (resolvedType == null)
            {
                return "{\"error\":\"Failed to resolve type\"}";
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
                return "{\"error\":\"Failed to find method in type\"}";
            }
            Logger.Debug("[Diver] Hook Method - Resolved Method");

            // We're all good regarding the signature!
            // assign subscriber unique id
            int token = AssignCallbackToken();
            Logger.Debug($"[Diver] Hook Method - Assigned Token: {token}");

            ScubaDiver.Hooking.HarmonyWrapper.HookCallback handler = (obj, args) => InvokeControllerCallback(token, new object[2] { obj, args });

            Logger.Debug($"[Diver] Hooking function {methodName}...");
            try
            {
                ScubaDiver.Hooking.HarmonyWrapper.Instance.AddHook(methodInfo, pos, handler);
            }
            catch (Exception ex)
            {
                Logger.Debug($"[Diver] Failed to hook func {methodName}. Exception: {ex}");
                return "{\"error\":\"Failed insert the hook for the function. HarmonyWrapper.AddHook failed.\"}";
            }
            Logger.Debug($"[Diver] Hooked func {methodName}!");


            // Save all the registeration info so it can be removed later upon request
            _tokensToRegisteredEventHandlers[token] = new RegisteredMethodHookInfo()
            {
                OriginalHookedMethod = methodInfo,
                RegisteredProxy = handler
            };

            EventRegistrationResults erResults = new EventRegistrationResults() { Token = token };
            return JsonConvert.SerializeObject(erResults);
        }


        private string MakeEventSubscribeResponse(HttpListenerRequest arg)
        {
            if (!HasCallbackEndpoint)
            {
                return "{\"error\":\"Callbacks endpoint missing. You must call /register_callbacks_ep before using this method!\"}";
            }

            string objAddrStr = arg.QueryString.Get("address");
            if (objAddrStr == null || !ulong.TryParse(objAddrStr, out var objAddr))
            {
                return "{\"error\":\"Missing parameter 'address'\"}";
            }
            Logger.Debug($"[Diver][Debug](RegisterEventHandler) objAddrStr={objAddr:X16}");

            // Check if we have this objects in our pinned pool
            FrozenObjectInfo foi;
            if (!TryGetPinnedObject(objAddr, out foi))
            {
                // Object not pinned, try get it the hard way
                return "{\"error\":\"Object at given address wasn't pinned\"}";
            }

            object target = foi.Object;
            Type resolvedType = target.GetType();

            string eventName = arg.QueryString.Get("event");
            if (eventName == null)
            {
                return "{\"error\":\"Missing parameter 'event'\"}";
            }
            // TODO: Does this need to be done recursivly?
            EventInfo eventObj = resolvedType.GetEvent(eventName);
            if (eventObj == null)
            {
                return "{\"error\":\"Failed to find event in type\"}";
            }

            // Let's make sure the event's delegate type has 2 args - (object, EventArgs or subclass)
            Type eventDelegateType = eventObj.EventHandlerType;
            MethodInfo invokeInfo = eventDelegateType.GetMethod("Invoke");
            ParameterInfo[] paramInfos = invokeInfo.GetParameters();
            if (paramInfos.Length != 2)
            {
                return "{\"error\":\"Currently only events with 2 parameters (object & EventArgs) can be subscribed to.\"}";
            }
            // Now I want to make sure the types of the parameters are subclasses of the expected ones.
            // Every type is a subclass of object so I skip the first param
            ParameterInfo secondParamInfo = paramInfos[1];
            Type secondParamType = secondParamInfo.ParameterType;
            if (!secondParamType.IsAssignableFrom(typeof(EventArgs)))
            {
                return "{\"error\":\"Second parameter of the event's handler was not a subclass of EventArgs\"}";
            }

            // TODO: Make sure delegate's return type is void? (Who even uses non-void delegates?)

            // We're all good regarding the signature!
            // assign subscriber unique id
            int token = AssignCallbackToken();

            EventHandler handler = (obj, args) => InvokeControllerCallback(token, new object[2] { obj, args });

            Logger.Debug($"[Diver] Adding event handler to event {eventName}...");
            eventObj.AddEventHandler(target, handler);
            Logger.Debug($"[Diver] Added event handler to event {eventName}!");


            // Save all the registeration info so it can be removed later upon request
            _tokensToRegisteredEventHandlers[token] = new RegisteredEventHandlerInfo()
            {
                EventInfo = eventObj,
                Target = target,
                RegisteredProxy = handler
            };

            EventRegistrationResults erResults = new EventRegistrationResults() { Token = token };
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
            IPAddress ipa;
            if (!IPAddress.TryParse(ipAddrStr, out ipa))
            {
                return "{\"error\":\"Parameter 'ip' couldn't be parsed to a valid IP Address\"}";
            }
            string portAddrStr = arg.QueryString.Get("port");
            int port;
            if (!int.TryParse(portAddrStr, out port))
            {
                return "{\"error\":\"Parameter 'port' couldn't be parsed to a valid IP Address\"}";
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
                return "{\"error\":\"Missing parameter 'address'\"}";
            }
            Logger.Debug($"[Diver][Debug](UnpinObject) objAddrStr={objAddr:X16}\n" +
                $"\tSOURCE : {(new System.Diagnostics.StackTrace()).GetFrame(1).GetMethod().Name}\n" +
                $"\tSOURCE2: {(new System.Diagnostics.StackTrace()).GetFrame(2).GetMethod().Name}");

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
                return "{\"error\":\"Object at given address wasn't pinned\"}";
            }
        }
        private string MakeCreateObjectResponse(HttpListenerRequest arg)
        {
            Logger.Debug("[Diver] Got /create_object request!");
            string body = null;
            using (StreamReader sr = new StreamReader(arg.InputStream))
            {
                body = sr.ReadToEnd();
            }

            if (string.IsNullOrEmpty(body))
            {
                return "{\"error\":\"Missing body\"}";
            }

            TextReader textReader = new StringReader(body);
            JsonReader jr = new JsonTextReader(textReader);
            JsonSerializer js = new JsonSerializer();
            var request = js.Deserialize<CtorInvocationRequest>(jr);
            if (request == null)
            {
                return "{\"error\":\"Failed to deserialize body\"}";
            }


            Type t = null;
            lock (_debugObjectsLock)
            {
                t = TypesResolver.Resolve(_runtime, request.TypeFullName);
            }
            if (t == null)
            {
                return "{\"error\":\"Failed to resolve type\"}";
            }

            List<object> paramsList = new List<object>();
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
                return "{\"error\":\"Activator.CreateInstance threw an exception\"}";
            }

            if (createdObject == null)
            {
                return "{\"error\":\"Activator.CreateInstance returned null\"}";
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


            InvocationResults invoRes = new InvocationResults()
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
            using (StreamReader sr = new StreamReader(arg.InputStream))
            {
                body = sr.ReadToEnd();
            }

            if (string.IsNullOrEmpty(body))
            {
                return "{\"error\":\"Missing body\"}";
            }

            TextReader textReader = new StringReader(body);
            JsonReader jr = new JsonTextReader(textReader);
            JsonSerializer js = new JsonSerializer();
            var request = js.Deserialize<InvocationRequest>(jr);
            if (request == null)
            {
                return "{\"error\":\"Failed to deserialize body\"}";
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
                        return "{\"error\":\"'address' points at an invalid address\"}";
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
                            "{\"error\":\"Object moved since last refresh. 'address' now points at an invalid address.\"}";
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
                            "{\"error\":\"Couldn't get handle to requested object. It could be because the Method Table mismatched or a GC collection happened.\"}";
                    }
                }
            }


            //
            // We have our target and it's type. No look for a matching overload for the
            // function to invoke.
            //
            List<object> paramsList = new List<object>();
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
                return "{\"error\":\"Couldn't find method in type.\"}";
            }

            string argsSummary = string.Join(", ", argumentTypes.Select(arg => arg.Name));
            Logger.Debug($"[Diver] Resolved method: {method.Name}({argsSummary}), Containing Type: {method.DeclaringType}");

            object results = null;
            try
            {
                argsSummary = string.Join(", ", paramsList.Select(param => param.ToString()));
                Logger.Debug($"[Diver] Invoking {method.Name} with those args: {argsSummary}");
                results = method.Invoke(instance, paramsList.ToArray());
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
            return JsonConvert.SerializeObject(invocResults);
        }
        private string MakeGetFieldResponse(HttpListenerRequest arg)
        {
            Logger.Debug("[Diver] Got /get_field request!");
            string body = null;
            object results = null;
            using (StreamReader sr = new StreamReader(arg.InputStream))
            {
                body = sr.ReadToEnd();
            }

            if (string.IsNullOrEmpty(body))
            {
                return "{\"error\":\"Missing body\"}";
            }

            TextReader textReader = new StringReader(body);
            JsonReader jr = new JsonTextReader(textReader);
            JsonSerializer js = new JsonSerializer();
            FieldSetRequest request = js.Deserialize<FieldSetRequest>(jr);
            if (request == null)
            {
                return "{\"error\":\"Failed to deserialize body\"}";
            }

            // Need to figure target instance and the target type.
            // In case of a static call the target instance stays null.
            Type dumpedObjType;
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
                    return "{\"error\":\"Trying to get field with a null target bu the field was not a static one\"}";
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
                    return "{\"error\":\"Can't get field of a unpinned objects\"}";
                }

                // Search the method with the matching signature
                var fieldInfo = dumpedObjType.GetFieldRecursive(request.FieldName);
                if (fieldInfo == null)
                {
                    Debugger.Launch();
                    Logger.Debug($"[Diver] Failed to Resolved field :/");
                    return "{\"error\":\"Couldn't find field in type.\"}";
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
            using (StreamReader sr = new StreamReader(arg.InputStream))
            {
                body = sr.ReadToEnd();
            }

            if (string.IsNullOrEmpty(body))
            {
                return "{\"error\":\"Missing body\"}";
            }

            TextReader textReader = new StringReader(body);
            JsonReader jr = new JsonTextReader(textReader);
            JsonSerializer js = new JsonSerializer();
            var request = js.Deserialize<FieldSetRequest>(jr);
            if (request == null)
            {
                return "{\"error\":\"Failed to deserialize body\"}";
            }

            // Need to figure target instance and the target type.
            // In case of a static call the target instance stays null.
            object instance = null;
            Type dumpedObjType;
            if (request.ObjAddress == 0)
            {
                return "{\"error\":\"Can't set field of a null target\"}";
            }

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
                }
                if (clrObj.Type == null)
                {
                    return "{\"error\":\"'address' points at an invalid address\"}";
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
                        "{\"error\":\"Object moved since last refresh. 'address' now points at an invalid address.\"}";
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
                        "{\"error\":\"Couldn't get handle to requested object. It could be because the Method Table or a GC collection happened.\"}";
                }
            }

            // Search the method with the matching signature
            var fieldInfo = dumpedObjType.GetFieldRecursive(request.FieldName);
            if (fieldInfo == null)
            {
                Debugger.Launch();
                Logger.Debug($"[Diver] Failed to Resolved field :/");
                return "{\"error\":\"Couldn't find field in type.\"}";
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
                return "{\"error\":\"Parameter 'address' could not be parsed as ulong\"}";
            }
            if (!int.TryParse(indexStr, out var index))
            {
                return "{\"error\":\"Parameter 'index' could not be parsed as ulong\"}";
            }

            // Check if we have this objects in our pinned pool
            FrozenObjectInfo arrayFoi;
            if (!TryGetPinnedObject(objAddr, out arrayFoi))
            {
                // Object not pinned, try get it the hard way
                return "{\"error\":\"Object at given address wasn't pinned\"}";
            }

            Array enumerable = (arrayFoi.Object as Array);
            object[] asArray = enumerable?.Cast<object>().ToArray();
            if (asArray == null)
            {
                return "{\"error\":\"Object at given address wasn't an array\"}";
            }
            if (index >= asArray.Length)
            {
                return "{\"error\":\"Index out of range\"}";
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
                FrozenObjectInfo itemFoi;
                if (IsPinned(item, out itemFoi))
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


            InvocationResults invokeRes = new InvocationResults()
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
                return "{\"error\":\"Missing parameter 'address'\"}";
            }
            if (!ulong.TryParse(objAddrStr, out var objAddr))
            {
                return "{\"error\":\"Parameter 'address' could not be parsed as ulong\"}";
            }
            if (hashCodeFallback)
            {
                if (!int.TryParse(hashCodeStr, out userHashcode))
                {
                    return "{\"error\":\"Parameter 'hashcode_fallback' was 'true' but the hashcode argument was missing or not an int\"}";
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
                return "{\"error\":\"No object in this address. Try finding it's address again and dumping again.\"}";
            }

            // Make sure it's still in place by refreshing the runtime
            RefreshRuntime();
            ClrObject clrObj =default(ClrObject);
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
                return "{\"error\":\"Method Table value mismatched\"}";
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
                return "{\"error\":\"No assemblies found matching the query\"}";
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


            TypesDump dump = new TypesDump()
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

            HeapDump hd = new HeapDump() { Objects = objects };

            var resJson = JsonConvert.SerializeObject(hd);
            return resJson;
        }
        private string MakeDomainsResponse(HttpListenerRequest req)
        {
            // TODO: Allow moving between domains?
            List<DomainsDump.AvailableDomain> available = new List<DomainsDump.AvailableDomain>();
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

            DomainsDump dd = new DomainsDump()
            {
                Current = AppDomain.CurrentDomain.FriendlyName,
                AvailableDomains = available
            };

            return JsonConvert.SerializeObject(dd);
        }
        private string MakeTypeResponse(HttpListenerRequest req)
        {
            string type = req.QueryString.Get("name");
            if (string.IsNullOrEmpty(type))
            {
                return "{\"error\":\"Missing parameter 'name'\"}";
            }

            string assembly = req.QueryString.Get("assembly");
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

                var methods = typeObj.GetMethods((BindingFlags)0xffff).Select(mi => new TypeDump.TypeMethod(mi))
                    .ToList();
                var fields = typeObj.GetFields((BindingFlags)0xffff).Select(fi => new TypeDump.TypeField(fi))
                    .ToList();
                var events = typeObj.GetEvents((BindingFlags)0xffff).Select(ei => new TypeDump.TypeEvent(ei))
                    .ToList();
                var props = typeObj.GetProperties((BindingFlags)0xffff).Select(pi => new TypeDump.TypeProperty(pi))
                    .ToList();

                TypeDump td = new TypeDump()
                {
                    Type = typeObj.FullName,
                    Assembly = typeObj.Assembly.GetName().Name,
                    Methods = methods,
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

            return "{\"error\":\"Failed to find type in searched assemblies\"}";
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