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
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Diagnostics.Runtime;
using ScubaDiver.API;
using ScubaDiver.API.Extensions;
using ScubaDiver.API.Hooking;
using ScubaDiver.API.Interactions;
using ScubaDiver.API.Interactions.Callbacks;
using ScubaDiver.API.Interactions.Client;
using ScubaDiver.API.Interactions.Dumps;
using ScubaDiver.API.Interactions.Object;
using ScubaDiver.API.Utils;
using ScubaDiver.Hooking;
using ScubaDiver.Utils;
using Exception = System.Exception;

namespace ScubaDiver
{
    public class Diver : IDisposable
    {
        // Runtime analysis and exploration fields
        private readonly object _clrMdLock = new();
        private DataTarget _dt = null;
        private ClrRuntime _runtime = null;
        // Address to Object converter
        private readonly Converter<object> _converter = new();

        // Clients Tracking
        public object _registeredPidsLock = new();
        public List<int> _registeredPids = new();

        // HTTP Responses fields
        private readonly Dictionary<string, Func<HttpListenerRequest, string>> _responseBodyCreators;

        // Callbacks Endpoint of the Controller process
        private bool _monitorEndpoints = true;
        private int _nextAvailableCallbackToken;
        private readonly UnifiedAppDomain _unifiedAppDomain;
        private readonly ConcurrentDictionary<int, RegisteredEventHandlerInfo> _remoteEventHandler;
        private readonly ConcurrentDictionary<int, RegisteredMethodHookInfo> _remoteHooks;

        // Object freezing (pinning)
        FrozenObjectsCollection _freezer = new FrozenObjectsCollection();

        private readonly ManualResetEvent _stayAlive = new(true);

        public Diver()
        {
            _responseBodyCreators = new Dictionary<string, Func<HttpListenerRequest, string>>()
            {
                // Divert maintenance
                {"/ping", MakePingResponse},
                {"/die", MakeDieResponse},
                {"/register_client", MakeRegisterClientResponse},
                {"/unregister_client", MakeUnregisterClientResponse},
                // DLL Injection
                {"/inject", MakeInjectResponse},
                // Dumping
                {"/domains", MakeDomainsResponse},
                {"/heap", MakeHeapResponse},
                {"/types", MakeTypesResponse},
                {"/type", MakeTypeResponse},
                // Remote Object API
                {"/object", MakeObjectResponse},
                {"/create_object", MakeCreateObjectResponse},
                {"/invoke", MakeInvokeResponse},
                {"/get_field", MakeGetFieldResponse},
                {"/set_field", MakeSetFieldResponse},
                {"/unpin", MakeUnpinResponse},
                {"/event_subscribe", MakeEventSubscribeResponse},
                {"/event_unsubscribe", MakeEventUnsubscribeResponse},
                {"/get_item", MakeArrayItemResponse},
                // Harmony
                {"/hook_method", MakeHookMethodResponse},
                {"/unhook_method", MakeUnhookMethodResponse},
            };
            _remoteEventHandler = new ConcurrentDictionary<int, RegisteredEventHandlerInfo>();
            _remoteHooks = new ConcurrentDictionary<int, RegisteredMethodHookInfo>();
            _unifiedAppDomain = new UnifiedAppDomain(this);
        }


        public void Start(ushort listenPort)
        {
            Logger.Debug("[Diver] Is logging debugs in release? " + Logger.DebugInRelease.Value);

            // Load or Hijack Newtonsoft.Json
            var nsJson = InitNewtonsoftJson();
            Logger.Debug("[Diver] Newtonsoft.Json's module path: " + nsJson.Location);

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

            Task endpointsMonitor = Task.Run(CallbacksEndpointsMonitor);
            Dispatcher(listener);
            Logger.Debug("[Diver] Stopping Callback Endpoints Monitor");
            _monitorEndpoints = false;
            try
            {
                endpointsMonitor.Wait();
            }
            catch
            {
                // IDC
            }

            Logger.Debug("[Diver] Closing listener");
            listener.Stop();
            listener.Close();
            Logger.Debug("[Diver] Closing ClrMD runtime and snapshot");
            lock (_clrMdLock)
            {
                _runtime?.Dispose();
                _runtime = null;
                _dt?.Dispose();
                _dt = null;
            }

            Logger.Debug("[Diver] Unpinning objects");
            _freezer.UnpinAll();
            Logger.Debug("[Diver] Unpinning finished");

            Logger.Debug("[Diver] Dispatcher returned, Start is complete.");
        }

        private void CallbacksEndpointsMonitor()
        {
            while (_monitorEndpoints)
            {
                Thread.Sleep(TimeSpan.FromSeconds(1));
                IPEndPoint endpoint;
                foreach (var registeredMethodHookInfo in _remoteHooks)
                {
                    endpoint = registeredMethodHookInfo.Value.Endpoint;
                    ReverseCommunicator reverseCommunicator = new(endpoint);
                    Logger.Debug($"[Diver] Checking if callback client at {endpoint} is alive. Token = {registeredMethodHookInfo.Key}. Type = Method Hook");
                    bool alive = reverseCommunicator.CheckIfAlive();
                    Logger.Debug($"[Diver] Callback client at {endpoint} (Token = {registeredMethodHookInfo.Key}) is alive = {alive}");
                    if (!alive)
                    {
                        Logger.Debug(
                            $"[Diver] Dead Callback client at {endpoint} (Token = {registeredMethodHookInfo.Key}) DROPPED!");
                        _remoteHooks.TryRemove(registeredMethodHookInfo.Key, out _);
                    }
                }
                foreach (var registeredEventHandlerInfo in _remoteEventHandler)
                {
                    endpoint = registeredEventHandlerInfo.Value.Endpoint;
                    ReverseCommunicator reverseCommunicator = new(endpoint);
                    Logger.Debug($"[Diver] Checking if callback client at {endpoint} is alive. Token = {registeredEventHandlerInfo.Key}. Type = Event");
                    bool alive = reverseCommunicator.CheckIfAlive();
                    Logger.Debug($"[Diver] Callback client at {endpoint} (Token = {registeredEventHandlerInfo.Key}) is alive = {alive}");
                    if (!alive)
                    {
                        Logger.Debug(
                            $"[Diver] Dead Callback client at {endpoint} (Token = {registeredEventHandlerInfo.Key}) DROPPED!");
                        _remoteEventHandler.TryRemove(registeredEventHandlerInfo.Key, out _);
                    }
                }
            }
        }

        #region Helpers
        void RefreshRuntime()
        {
            lock (_clrMdLock)
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
        private Assembly InitNewtonsoftJson()
        {
            // This will trigger our resolver to either get a pre-loaded Newtonsoft.Json version
            // (used by our target) or, if not found, load our own dll.
            Assembly ass = Assembly.Load(new AssemblyName("Newtonsoft.Json"));
            NewtonsoftProxy.Init(ass);
            return ass;
        }
        private object ParseParameterObject(ObjectOrRemoteAddress param)
        {
            switch (param)
            {
                case { IsNull: true }:
                    return null;
                case { IsType: true }:
                    return _unifiedAppDomain.ResolveType(param.Type, param.Assembly);
                case { IsRemoteAddress: false }:
                    return PrimitivesEncoder.Decode(param.EncodedObject, param.Type);
                case { IsRemoteAddress: true }:
                    if (_freezer.TryGetPinnedObject(param.RemoteAddress, out object pinnedObj))
                    {
                        return pinnedObj;
                    }
                    break;
            }

            Debugger.Launch();
            throw new NotImplementedException(
                $"Don't know how to parse this parameter into an object of type `{param.Type}`");
        }

        public string QuickError(string error, string stackTrace = null)
        {
            if (stackTrace == null)
            {
                stackTrace = (new StackTrace(true)).ToString();
            }
            DiverError errResults = new(error, stackTrace);
            return JsonConvert.SerializeObject(errResults);
        }

        #endregion

        #region HTTP Dispatching
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
                    body = QuickError(ex.Message, ex.StackTrace);
                }
            }
            else
            {
                body = QuickError("Unknown Command");
            }

            byte[] buffer = Encoding.UTF8.GetBytes(body);
            // Get a response stream and write the response to it.
            response.ContentLength64 = buffer.Length;
            response.ContentType = "application/json";
            Stream output = response.OutputStream;
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
                    try
                    {
                        HarmonyWrapper.Instance.RegisterFrameworkThread(Thread.CurrentThread.ManagedThreadId);

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
                    finally
                    {
                        HarmonyWrapper.Instance.UnregisterFrameworkThread(Thread.CurrentThread.ManagedThreadId);
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
                HarmonyWrapper.Instance.RemovePrefix(rmhi.OriginalHookedMethod);
            }
            _remoteEventHandler.Clear();
            _remoteHooks.Clear();
            Logger.Debug("[Diver] Removed all event subscriptions & hooks");
        }
        #endregion

        #region Object Pinning
        public (object instance, ulong pinnedAddress) GetObject(ulong objAddr, bool pinningRequested, int? hashcode = null)
        {
            bool hashCodeFallback = hashcode.HasValue;

            // Check if we have this objects in our pinned pool
            if (_freezer.TryGetPinnedObject(objAddr, out object pinnedObj))
            {
                // Found pinned object!
                return (pinnedObj, objAddr);
            }

            // Object not pinned, try get it the hard way
            // Make sure we had such an object in the last dumped runtime (this will help us later if the object moves
            // since we'll know what type we are looking for)
            // Make sure it's still in place
            ClrObject lastKnownClrObj = default;
            lock (_clrMdLock)
            {
                lastKnownClrObj = _runtime.Heap.GetObject(objAddr);
            }
            if (lastKnownClrObj == default)
            {
                throw new Exception("No object in this address. Try finding it's address again and dumping again.");
            }

            // Make sure it's still in place by refreshing the runtime
            RefreshRuntime();
            ClrObject clrObj = default;
            lock (_clrMdLock)
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
                    throw new Exception(
                        "{\"error\":\"Object moved since last refresh. 'address' now points at an invalid address. " +
                        "Hash Code fallback was NOT activated\"}");
                }

                Predicate<string> typeFilter = (string type) => type.Contains(lastKnownClrObj.Type.Name);
                (bool anyErrors, List<HeapDump.HeapObject> objects) = GetHeapObjects(typeFilter, true);
                if (anyErrors)
                {
                    throw new Exception(
                        "{\"error\":\"Object moved since last refresh. 'address' now points at an invalid address. " +
                        "Hash Code fallback was activated but dumping function failed so non hash codes were checked\"}");
                }
                var matches = objects.Where(heapObj => heapObj.HashCode == hashcode.Value).ToList();
                if (matches.Count != 1)
                {
                    throw new Exception(
                        "{\"error\":\"Object moved since last refresh. 'address' now points at an invalid address. " +
                        $"Hash Code fallback was activated but {((matches.Count > 1) ? "too many (>1)" : "no")} objects with the same hash code were found\"}}");
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
                throw new Exception("Method Table value mismatched");
            }


            // Pin the result object if requested
            ulong pinnedAddress = 0;
            if (pinningRequested)
            {
                pinnedAddress = _freezer.Pin(instance);
            }
            return (instance, pinnedAddress);
        }

        #endregion

        public (bool anyErrors, List<HeapDump.HeapObject> objects) GetHeapObjects(Predicate<string> filter, bool dumpHashcodes)
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
                lock (_clrMdLock)
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

                            if (dumpHashcodes)
                            {
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
                                // now getting the hashcode which is itself a challenge since 
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

        #region DLL Injecion Handler

        private string MakeInjectResponse(HttpListenerRequest req)
        {
            string dllPath = req.QueryString.Get("dll_path");
            try
            {
                var asm = Assembly.LoadFile(dllPath);
                // We must request all Types or otherwise the Type object won't be created
                // (I think there's some lazy initialization behind the scenes)
                var allTypes = asm.GetTypes();
                // This will prevent the compiler from removing the above lines because of "unused code"
                GC.KeepAlive(allTypes);
                
                // ClrMD must take a new snapshot to see our new assembly
                RefreshRuntime();

                return "{\"status\":\"dll loaded\"}";
            }
            catch (Exception ex)
            {
                return QuickError(ex.Message, ex.StackTrace);
            }
        }

        #endregion


        #region Ping Handler

        private string MakePingResponse(HttpListenerRequest arg)
        {
            return "{\"status\":\"pong\"}";
        }

        #endregion

        #region Client Registration Handlers
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

        #endregion

        private string MakeDomainsResponse(HttpListenerRequest req)
        {
            List<DomainsDump.AvailableDomain> available = new();
            lock (_clrMdLock)
            {
                foreach (ClrAppDomain clrAppDomain in _runtime.AppDomains)
                {
                    available.Add(new DomainsDump.AvailableDomain()
                    {
                        Name = clrAppDomain.Name,
                        AvailableModules = clrAppDomain.Modules
                            .Select(m => Path.GetFileNameWithoutExtension(m.Name))
                            .Where(m => !string.IsNullOrWhiteSpace(m))
                            .ToList()
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
        private string MakeTypesResponse(HttpListenerRequest req)
        {
            string assembly = req.QueryString.Get("assembly");

            // Try exact match assembly 
            IEnumerable<ClrModule> allAssembliesInApp = null;
            lock (_clrMdLock)
            {
                allAssembliesInApp = _runtime.AppDomains.SelectMany(appDom => appDom.Modules);
            }
            List<ClrModule> matchingAssemblies = allAssembliesInApp.Where(module => Path.GetFileNameWithoutExtension(module.Name) == assembly).ToList();
            if (matchingAssemblies.Count == 0)
            {
                // No exact matches, widen search to any assembly *containing* the query
                matchingAssemblies = allAssembliesInApp.Where(module =>
                {
                    try
                    {
                        return Path.GetFileNameWithoutExtension(module.Name).Contains(assembly);
                    }
                    catch
                    {
                    }

                    return false;
                }).ToList();
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
        public string MakeTypeResponse(TypeDumpRequest dumpRequest)
        {
            string type = dumpRequest.TypeFullName;
            if (string.IsNullOrEmpty(type))
            {
                return QuickError("Missing parameter 'TypeFullName'");
            }

            string assembly = dumpRequest.Assembly;
            //Logger.Debug($"[Diver] Trying to dump Type: {type}");
            if (assembly != null)
            {
                //Logger.Debug($"[Diver] Trying to dump Type: {type}, WITH Assembly: {assembly}");
            }
            Type resolvedType = null;
            lock (_clrMdLock)
            {
                resolvedType = _unifiedAppDomain.ResolveType(type, assembly);
            }

            // 
            // Defining a sub-function that parses a type and it's parents recursively
            //
            static TypeDump ParseType(Type typeObj)
            {
                if (typeObj == null) return null;

                var ctors = typeObj.GetConstructors((BindingFlags)0xffff).Select(ci => new TypeDump.TypeMethod(ci))
                    .ToList();
                var methods = typeObj.GetRuntimeMethods().Select(mi => new TypeDump.TypeMethod(mi))
                    .ToList();
                var fields = typeObj.GetRuntimeFields().Select(fi => new TypeDump.TypeField(fi))
                    .ToList();
                var events = typeObj.GetRuntimeEvents().Select(ei => new TypeDump.TypeEvent(ei))
                    .ToList();
                var props = typeObj.GetRuntimeProperties().Select(pi => new TypeDump.TypeProperty(pi))
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
                if (typeObj.BaseType != null)
                {
                    // Has parent. Add its identifier
                    td.ParentFullTypeName = typeObj.BaseType.FullName;
                    td.ParentAssembly = typeObj.BaseType.Assembly.GetName().Name;
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
        private string MakeTypeResponse(HttpListenerRequest req)
        {
            string body = null;
            using (StreamReader sr = new(req.InputStream))
            {
                body = sr.ReadToEnd();
            }
            if (string.IsNullOrEmpty(body))
            {
                return QuickError("Missing body");
            }

            TextReader textReader = new StringReader(body);
            var request = JsonConvert.DeserializeObject<TypeDumpRequest>(body);
            if (request == null)
            {
                return QuickError("Failed to deserialize body");
            }

            return MakeTypeResponse(request);
        }
        private string MakeHeapResponse(HttpListenerRequest arg)
        {
            string filter = arg.QueryString.Get("type_filter");
            string dumpHashcodesStr = arg.QueryString.Get("dump_hashcodes");
            bool dumpHashcodes = dumpHashcodesStr?.ToLower() == "true";

                                 // Default filter - no filter. Just return everything.
            Predicate<string> matchesFilter = Filter.CreatePredicate(filter);

            (bool anyErrors, List<HeapDump.HeapObject> objects) = GetHeapObjects(matchesFilter, dumpHashcodes);
            if (anyErrors)
            {
                return "{\"error\":\"All dumping trials failed because at least 1 " +
                       "object moved between the snapshot and the heap enumeration\"}";
            }

            HeapDump hd = new() { Objects = objects };

            var resJson = JsonConvert.SerializeObject(hd);
            return resJson;
        }
        #region Hooks & Events Handlers

        private string MakeUnhookMethodResponse(HttpListenerRequest arg)
        {
            string tokenStr = arg.QueryString.Get("token");
            if (tokenStr == null || !int.TryParse(tokenStr, out int token))
            {
                return QuickError("Missing parameter 'address'");
            }
            Logger.Debug($"[Diver][MakeUnhookMethodResponse] Called! Token: {token}");

            if (_remoteHooks.TryRemove(token, out RegisteredMethodHookInfo rmhi))
            {
                HarmonyWrapper.Instance.RemovePrefix(rmhi.OriginalHookedMethod);
                return "{\"status\":\"OK\"}";
            }

            Logger.Debug($"[Diver][MakeUnhookMethodResponse] Unknown token for event callback subscription. Token: {token}");
            return QuickError("Unknown token for event callback subscription");
        }
        private string MakeHookMethodResponse(HttpListenerRequest arg)
        {
            Logger.Debug("[Diver] Got Hook Method request!");
            string body;
            using (StreamReader sr = new(arg.InputStream))
            {
                body = sr.ReadToEnd();
            }

            if (string.IsNullOrEmpty(body))
                return QuickError("Missing body");

            var request = JsonConvert.DeserializeObject<FunctionHookRequest>(body);
            if (request == null)
                return QuickError("Failed to deserialize body");

            if (!IPAddress.TryParse(request.IP, out IPAddress ipAddress))
            {
                return QuickError("Failed to parse IP address. Input: " + request.IP);
            }
            int port = request.Port;
            IPEndPoint endpoint = new IPEndPoint(ipAddress, port);
            string typeFullName = request.TypeFullName;
            string methodName = request.MethodName;
            string hookPosition = request.HookPosition;
            HarmonyPatchPosition pos = (HarmonyPatchPosition)Enum.Parse(typeof(HarmonyPatchPosition), hookPosition);
            if (!Enum.IsDefined(typeof(HarmonyPatchPosition), pos))
                return QuickError("hook_position has an invalid or unsupported value");

            Type resolvedType;
            lock (_clrMdLock)
            {
                resolvedType = _unifiedAppDomain.ResolveType(typeFullName);
            }
            if (resolvedType == null)
                return QuickError("Failed to resolve type");

            Type[] paramTypes;
            lock (_clrMdLock)
            {
                paramTypes = request.ParametersTypeFullNames.Select(typeFullName => _unifiedAppDomain.ResolveType(typeFullName)).ToArray();
            }

            // We might be searching for a constructor. Switch based on method name.
            MethodBase methodInfo = methodName == ".ctor"
                ? resolvedType.GetConstructor(paramTypes)
                : resolvedType.GetMethodRecursive(methodName, paramTypes);

            if (methodInfo == null)
                return QuickError($"Failed to find method {methodName} in type {resolvedType.Name}");
            Logger.Debug("[Diver] Hook Method - Resolved Method");

            // We're all good regarding the signature!
            // assign subscriber unique id
            int token = AssignCallbackToken();
            Logger.Debug($"[Diver] Hook Method - Assigned Token: {token}");

            // Preparing a proxy method that Harmony will invoke
            HarmonyWrapper.HookCallback patchCallback = (obj, args) => InvokeControllerCallback(endpoint, token, new StackTrace().ToString(), obj, args);

            Logger.Debug($"[Diver] Hooking function {methodName}...");
            try
            {
                HarmonyWrapper.Instance.AddHook(methodInfo, pos, patchCallback);
            }
            catch (Exception ex)
            {
                // Hooking filed so we cleanup the Hook Info we inserted beforehand 
                _remoteHooks.TryRemove(token, out _);

                Logger.Debug($"[Diver] Failed to hook func {methodName}. Exception: {ex}");
                return QuickError("Failed insert the hook for the function. HarmonyWrapper.AddHook failed.");
            }
            Logger.Debug($"[Diver] Hooked func {methodName}!");

            // Keeping all hooking information aside so we can unhook later.
            _remoteHooks[token] = new RegisteredMethodHookInfo()
            {
                Endpoint = endpoint,
                OriginalHookedMethod = methodInfo,
                RegisteredProxy = patchCallback
            };


            EventRegistrationResults erResults = new() { Token = token };
            return JsonConvert.SerializeObject(erResults);
        }
        private string MakeEventUnsubscribeResponse(HttpListenerRequest arg)
        {
            string tokenStr = arg.QueryString.Get("token");
            if (tokenStr == null || !int.TryParse(tokenStr, out int token))
            {
                return QuickError("Missing parameter 'address'");
            }
            Logger.Debug($"[Diver][MakeEventUnsubscribeResponse] Called! Token: {token}");

            if (_remoteEventHandler.TryRemove(token, out RegisteredEventHandlerInfo eventInfo))
            {
                eventInfo.EventInfo.RemoveEventHandler(eventInfo.Target, eventInfo.RegisteredProxy);
                return "{\"status\":\"OK\"}";
            }
            return QuickError("Unknown token for event callback subscription");
        }
        private string MakeEventSubscribeResponse(HttpListenerRequest arg)
        {
            string objAddrStr = arg.QueryString.Get("address");
            string ipAddrStr = arg.QueryString.Get("ip");
            string portStr = arg.QueryString.Get("port");
            if (objAddrStr == null || !ulong.TryParse(objAddrStr, out var objAddr))
            {
                return QuickError("Missing parameter 'address' (object address)");
            }
            if (!IPAddress.TryParse(ipAddrStr, out IPAddress ipAddress) || int.TryParse(portStr, out int port))
            {
                return QuickError("Failed to parse either IP Address ('ip' param) or port ('port' param)");
            }
            IPEndPoint endpoint = new IPEndPoint(ipAddress, port);
            Logger.Debug($"[Diver][Debug](RegisterEventHandler) objAddrStr={objAddr:X16}");

            // Check if we have this objects in our pinned pool
            if (!_freezer.TryGetPinnedObject(objAddr, out object target))
            {
                // Object not pinned, try get it the hard way
                return QuickError("Object at given address wasn't pinned");
            }

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

            EventHandler eventHandler = (obj, args) => InvokeControllerCallback(endpoint, token, "UNUSED", new object[2] { obj, args });

            Logger.Debug($"[Diver] Adding event handler to event {eventName}...");
            eventObj.AddEventHandler(target, eventHandler);
            Logger.Debug($"[Diver] Added event handler to event {eventName}!");


            // Save all the registeration info so it can be removed later upon request
            _remoteEventHandler[token] = new RegisteredEventHandlerInfo()
            {
                EventInfo = eventObj,
                Target = target,
                RegisteredProxy = eventHandler,
                Endpoint = endpoint
            };

            EventRegistrationResults erResults = new() { Token = token };
            return JsonConvert.SerializeObject(erResults);
        }
        public int AssignCallbackToken() => Interlocked.Increment(ref _nextAvailableCallbackToken);
        public void InvokeControllerCallback(IPEndPoint callbacksEndpoint, int token, string stackTrace, params object[] parameters)
        {
            ReverseCommunicator reverseCommunicator = new(callbacksEndpoint);

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
                    // Check fi the object was pinned
                    if (!_freezer.TryGetPinningAddress(parameter, out ulong addr))
                    {
                        // Pin and mark for unpinning later
                        addr = _freezer.Pin(parameter);
                    }

                    remoteParams[i] = ObjectOrRemoteAddress.FromToken(addr, parameter.GetType().FullName);
                }
            }

            // Call callback at controller
            reverseCommunicator.InvokeCallback(token, stackTrace, remoteParams);
        }

        #endregion
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

            try
            {
                (object instance, ulong pinnedAddress) = GetObject(objAddr, pinningRequested, hashCodeFallback ? userHashcode : null);
                ObjectDump od = ObjectDumpFactory.Create(instance, objAddr, pinnedAddress);
                return JsonConvert.SerializeObject(od);
            }
            catch (Exception e)
            {
                return QuickError("Failed Getting the object for the user. Error: " + e.Message);
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

            var request = JsonConvert.DeserializeObject<CtorInvocationRequest>(body);
            if (request == null)
            {
                return QuickError("Failed to deserialize body");
            }


            Type t = null;
            lock (_clrMdLock)
            {
                t = _unifiedAppDomain.ResolveType(request.TypeFullName);
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
                HarmonyWrapper.Instance.AllowFrameworkThreadToTrigger(Thread.CurrentThread.ManagedThreadId);
                createdObject = Activator.CreateInstance(t, paramsArray);
            }
            catch
            {
                Debugger.Launch();
                return QuickError("Activator.CreateInstance threw an exception");
            }
            finally
            {
                HarmonyWrapper.Instance.DisallowFrameworkThreadToTrigger(Thread.CurrentThread.ManagedThreadId);
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
                pinAddr = _freezer.Pin(createdObject);
                res = ObjectOrRemoteAddress.FromToken(pinAddr, createdObject.GetType().FullName);
            }


            InvocationResults invoRes = new()
            {
                ReturnedObjectOrAddress = res,
                VoidReturnType = false
            };
            return JsonConvert.SerializeObject(invoRes);

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
            var request = JsonConvert.DeserializeObject<InvocationRequest>(body);
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

                lock (_clrMdLock)
                {
                    dumpedObjType = _unifiedAppDomain.ResolveType(request.TypeFullName);
                }
            }
            else
            {
                //
                // Non-null target object address. Non-static call
                //

                // Check if we have this objects in our pinned pool
                if (_freezer.TryGetPinnedObject(request.ObjAddress, out instance))
                {
                    // Found pinned object!
                    dumpedObjType = instance.GetType();
                }
                else
                {
                    // Object not pinned, try get it the hard way
                    ClrObject clrObj;
                    lock (_clrMdLock)
                    {
                        clrObj = _runtime.Heap.GetObject(request.ObjAddress);
                    }
                    if (clrObj.Type == null)
                    {
                        return QuickError("'address' points at an invalid address");
                    }

                    // Make sure it's still in place
                    RefreshRuntime();
                    lock (_clrMdLock)
                    {
                        clrObj = _runtime.Heap.GetObject(request.ObjAddress);
                    }
                    if (clrObj.Type == null)
                    {
                        return
                            QuickError("Object moved since last refresh. 'address' now points at an invalid address.");
                    }

                    ulong mt = clrObj.Type.MethodTable;
                    dumpedObjType = _unifiedAppDomain.ResolveType(clrObj.Type.Name);
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

            // Infer parameter types from received parameters.
            // Note that for 'null' arguments we don't know the type so we use a "Wild Card" type
            Type[] argumentTypes = paramsList.Select(p => p?.GetType() ?? new WildCardType()).ToArray();

            // Get types of generic arguments <T1,T2, ...>
            Type[] genericArgumentTypes = request.GenericArgsTypeFullNames.Select(typeFullName => _unifiedAppDomain.ResolveType(typeFullName)).ToArray();

            // Search the method with the matching signature
            var method = dumpedObjType.GetMethodRecursive(request.MethodName, genericArgumentTypes, argumentTypes);
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
                HarmonyWrapper.Instance.AllowFrameworkThreadToTrigger(Thread.CurrentThread.ManagedThreadId);
                results = method.Invoke(instance, paramsList.ToArray());
            }
            catch (Exception e)
            {
                return QuickError($"Invocation caused exception: {e}");
            }
            finally
            {
                HarmonyWrapper.Instance.DisallowFrameworkThreadToTrigger(Thread.CurrentThread.ManagedThreadId);
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
                        ulong resultsAddress = _freezer.Pin(results);
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
            FieldSetRequest request = JsonConvert.DeserializeObject<FieldSetRequest>(body);
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
                lock (_clrMdLock)
                {
                    dumpedObjType = _unifiedAppDomain.ResolveType(request.TypeFullName);
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
                if (_freezer.TryGetPinnedObject(request.ObjAddress, out instance))
                {
                    // Found pinned object!
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
                    return QuickError($"Invocation caused exception: {e}");
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
                    ulong resultsAddress = _freezer.Pin(results);
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

            var request = JsonConvert.DeserializeObject<FieldSetRequest>(body);
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
            if (_freezer.TryGetPinnedObject(request.ObjAddress, out instance))
            {
                // Found pinned object!
                dumpedObjType = instance.GetType();
            }
            else
            {
                // Object not pinned, try get it the hard way
                ClrObject clrObj = default;
                lock (_clrMdLock)
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
                dumpedObjType = _unifiedAppDomain.ResolveType(clrObj.Type.Name);
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
                return QuickError($"Invocation caused exception: {e}");
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
                    ulong resultsAddress = _freezer.Pin(results);
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
            string body = null;
            using (StreamReader sr = new(arg.InputStream))
            {
                body = sr.ReadToEnd();
            }

            if (string.IsNullOrEmpty(body))
                return QuickError("Missing body");

            var request = JsonConvert.DeserializeObject<IndexedItemAccessRequest>(body);
            if (request == null)
                return QuickError("Failed to deserialize body");

            ulong objAddr = request.CollectionAddress;
            object index = ParseParameterObject(request.Index);
            bool pinningRequested = request.PinRequest;

            // Check if we have this objects in our pinned pool
            if (!_freezer.TryGetPinnedObject(objAddr, out object pinnedObj))
            {
                // Object not pinned, try get it the hard way
                return QuickError("Object at given address wasn't pinned");
            }

            object item = null;
            if (pinnedObj.GetType().IsArray)
            {
                Array asArray = (Array)pinnedObj;
                if (index is not int intIndex)
                    return QuickError("Tried to access an Array with a non-int index");

                int length = asArray.Length;
                if (intIndex >= length)
                    return QuickError("Index out of range");

                item = asArray.GetValue(intIndex);
            }
            else if (pinnedObj is IList asList)
            {
                object[] asArray = asList?.Cast<object>().ToArray();
                if (asArray == null)
                    return QuickError("Object at given address seemed to be an IList but failed to convert to array");

                if (index is not int intIndex)
                    return QuickError("Tried to access an IList with a non-int index");

                int length = asArray.Length;
                if (intIndex >= length)
                    return QuickError("Index out of range");

                // Get the item
                item = asArray[intIndex];
            }
            else if (pinnedObj is IDictionary dict)
            {
                Logger.Debug("[Diver] Array access: Object is an IDICTIONARY!");
                item = dict[index];
            }
            else if (pinnedObj is IEnumerable enumerable)
            {
                // Last result - generic IEnumerables can be enumerated into arrays.
                // BEWARE: This could lead to "runining" of the IEnumerable if it's a not "resetable"
                object[] asArray = enumerable?.Cast<object>().ToArray();
                if (asArray == null)
                    return QuickError("Object at given address seemed to be an IEnumerable but failed to convert to array");

                if (index is not int intIndex)
                    return QuickError("Tried to access an IEnumerable (which isn't an Array, IList or IDictionary) with a non-int index");

                int length = asArray.Length;
                if (intIndex >= length)
                    return QuickError("Index out of range");

                // Get the item
                item = asArray[intIndex];
            }
            else
            {
                Logger.Debug("[Diver] Array access: Object isn't an Array, IList, IDictionary or IEnumerable");
                return QuickError("Object isn't an Array, IList, IDictionary or IEnumerable");
            }

            ObjectOrRemoteAddress res;
            if (item == null)
            {
                res = ObjectOrRemoteAddress.Null;
            }
            else if (item.GetType().IsPrimitiveEtc())
            {
                // TODO: Something else?
                res = ObjectOrRemoteAddress.FromObj(item);
            }
            else
            {
                // Non-primitive results must be pinned before returning their remote address
                // TODO: If a RemoteObject is not created for this object later and the item is not automaticlly unfreezed it might leak.
                if (!_freezer.TryGetPinningAddress(item, out ulong addr))
                {
                    // Item not pinned yet, let's do it.
                    addr = _freezer.Pin(item);
                }

                res = ObjectOrRemoteAddress.FromToken(addr, item.GetType().FullName);
            }


            InvocationResults invokeRes = new()
            {
                VoidReturnType = false,
                ReturnedObjectOrAddress = res
            };


            return JsonConvert.SerializeObject(invokeRes);
        }
        private string MakeUnpinResponse(HttpListenerRequest arg)
        {
            string objAddrStr = arg.QueryString.Get("address");
            if (objAddrStr == null || !ulong.TryParse(objAddrStr, out var objAddr))
            {
                return QuickError("Missing parameter 'address'");
            }
            Logger.Debug($"[Diver][Debug](Unpin) objAddrStr={objAddr:X16}");

            // Check if we have this objects in our pinned pool
            if (_freezer.TryGetPinnedObject(objAddr, out _))
            {
                // Found pinned object!
                _freezer.Unpin(objAddr);
                return "{\"status\":\"OK\"}";
            }
            else
            {
                // Object not pinned, try get it the hard way
                return QuickError("Object at given address wasn't pinned");
            }
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

        // IDisposable
        public void Dispose()
        {
            lock (_clrMdLock)
            {
                _runtime?.Dispose();
                _dt?.Dispose();
            }
        }

    }
}