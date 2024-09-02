using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Reflection.Metadata;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Diagnostics.Runtime;
using MonoMod.Utils;
using ScubaDiver.API;
using ScubaDiver.API.Extensions;
using ScubaDiver.API.Hooking;
using ScubaDiver.API.Interactions;
using ScubaDiver.API.Interactions.Callbacks;
using ScubaDiver.API.Interactions.Dumps;
using ScubaDiver.API.Interactions.Object;
using ScubaDiver.API.Utils;
using ScubaDiver.Hooking;
using ScubaDiver.Utils;
using Exception = System.Exception;

namespace ScubaDiver
{
    public class DotNetDiver : DiverBase
    {
        // Runtime analysis and exploration fields
        private readonly object _clrMdLock = new();
        private DataTarget _dt = null;
        private ClrRuntime _runtime = null;
        // Address to Object converter
        private readonly Converter<object> _converter = new();


        // Callbacks Endpoint of the Controller process
        private readonly UnifiedAppDomain _unifiedAppDomain;
        private readonly ConcurrentDictionary<int, RegisteredEventHandlerInfo> _remoteEventHandler;


        // Object freezing (pinning)
        FrozenObjectsCollection _freezer = new FrozenObjectsCollection();

        public DotNetDiver(IRequestsListener listener) : base(listener)
        {
            _responseBodyCreators.Add("/event_subscribe", MakeEventSubscribeResponse);
            _responseBodyCreators.Add("/event_unsubscribe", MakeEventUnsubscribeResponse);

            _remoteEventHandler = new ConcurrentDictionary<int, RegisteredEventHandlerInfo>();
            _unifiedAppDomain = new UnifiedAppDomain(this);
        }

        private Task endpointsMonitor;

        public override void Start()
        {
            Logger.Debug("[DotNetDiver] Is logging debugs in release? " + Logger.DebugInRelease.Value);

            // Load or Hijack Newtonsoft.Json
            var nsJson = InitNewtonsoftJson();
            Logger.Debug("[DotNetDiver] Newtonsoft.Json's module path: " + nsJson.Location);

            // Trying GC Collect to overcome some problem where ClrMD won't "see" many existing objects
            // when the heap is dumped.
            GC.Collect();
            
            // Start session
            Logger.Debug("[DotNetDiver] Refreshing runtime...");
            RefreshRuntime();
            Logger.Debug("[DotNetDiver] Refreshed runtime");

            endpointsMonitor = Task.Run(CallbacksEndpointsMonitor);

            // This will runt the requests listener
            base.Start();
            Logger.Debug("[DotNetDiver] Started.");
        }

        protected override void CallbacksEndpointsMonitor()
        {
            while (_monitorEndpoints)
            {
                Thread.Sleep(TimeSpan.FromSeconds(1));
                IPEndPoint endpoint;
                foreach (var registeredMethodHookInfo in _remoteHooks)
                {
                    endpoint = registeredMethodHookInfo.Value.Endpoint;
                    ReverseCommunicator reverseCommunicator = new(endpoint);
                    //Logger.Debug($"[DotNetDiver] Checking if callback client at {endpoint} is alive. Token = {registeredMethodHookInfo.Key}. Type = Method Hook");
                    bool alive = reverseCommunicator.CheckIfAlive();
                    //Logger.Debug($"[DotNetDiver] Callback client at {endpoint} (Token = {registeredMethodHookInfo.Key}) is alive = {alive}");
                    if (!alive)
                    {
                        Logger.Debug($"[DotNetDiver] Dead Callback client at {endpoint} (Token = {registeredMethodHookInfo.Key}) DROPPED!");
                        _remoteHooks.TryRemove(registeredMethodHookInfo.Key, out _);
                    }
                }
                foreach (var registeredEventHandlerInfo in _remoteEventHandler)
                {
                    endpoint = registeredEventHandlerInfo.Value.Endpoint;
                    ReverseCommunicator reverseCommunicator = new(endpoint);
                    //Logger.Debug($"[DotNetDiver] Checking if callback client at {endpoint} is alive. Token = {registeredEventHandlerInfo.Key}. Type = Event");
                    bool alive = reverseCommunicator.CheckIfAlive();
                    //Logger.Debug($"[DotNetDiver] Callback client at {endpoint} (Token = {registeredEventHandlerInfo.Key}) is alive = {alive}");
                    if (!alive)
                    {
                        Logger.Debug($"[DotNetDiver] Dead Callback client at {endpoint} (Token = {registeredEventHandlerInfo.Key}) DROPPED!");
                        _remoteEventHandler.TryRemove(registeredEventHandlerInfo.Key, out _);
                    }
                }
            }
        }

        #region Helpers
        protected override void RefreshRuntime()
        {
            lock (_clrMdLock)
            {
                Logger.Debug("[RefreshRuntime] Called");
                var windowsProcessDataReader = _dt?.DataReader;
                int? pid = windowsProcessDataReader?.ProcessId;

                _runtime?.Dispose();
                _runtime = null;
                _dt?.Dispose();
                _dt = null;

                if (pid != null)
                {
                    try
                    {
                        Logger.Debug("[Info] Trying to kill snapshot with PID = " + pid);
                        Process.GetProcessById(pid.Value).Kill();
                        Logger.Debug("[Info] Killed snapshot with PID = " + pid);
                    }
                    catch(Exception ex)
                    {
                        if (ex.Message.Contains("is not running."))
                        {
                            // This is ok.
                        }
                        else
                        {
                            Logger.Debug("[ERROR] Trying to kill snapshot with PID = " + pid + "failed. Ex: " + ex);
                        }
                    }
                }
                else
                {
                    Logger.Debug("[ERROR] No PID of snapshot ???");
                }

                // This works like 'fork()', it does NOT create a dump file and uses it as the target
                // Instead it creates a secondary process which is a copy of the current one, but without any running threads.
                // Then our process attaches to the other one and reads its memory.
                //
                // NOTE: This subprocess inherits handles to DLLs in the current process so it might "lock"
                // both UnmanagedAdapterDLL.dll and ScubaDiver.dll
                _dt = DataTarget.CreateSnapshotAndAttach(Process.GetCurrentProcess().Id);
                try
                {
                    _runtime = _dt.ClrVersions.Single().CreateRuntime();
                }
                catch (Exception ex)
                {
                    Console.WriteLine("Exception while trying to find CLR runtime");
                    Console.WriteLine(ex);
                    Debugger.Launch();
                    Debugger.Break();
                }
            }
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

        #endregion

        protected override string MakeInjectDllResponse(ScubaDiverMessage req)
        {
            return QuickError("Not Implemented");
        }

        #region Object Pinning
        public (object instance, ulong pinnedAddress) GetObject(ulong objAddr, bool pinningRequested, string typeName, int? hashcode = null)
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
            if (clrObj.Type != null && clrObj.Type.Name == typeName)
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
                methodTable = heapObj.MethodTable();
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

            //
            // A GC collect might still happen between checking
            // the CLR MD object and the retrieval of the object
            // So we check the final object's type name one last time 
            // (It's better to crash here then return bad objects)
            //
            string finalTypeName;
            try
            {
                finalTypeName = instance.GetType().FullName;
            }
            catch (Exception ex)
            {
                throw new AggregateException(
                    "The final object we got from the address (after checking CLR MD twice) was broken " +
                    "and we couldn't read it's Type's full name.", ex);
            }

            if (finalTypeName != typeName)
                throw new Exception("A GC collection occurred between checking the CLR MD (twice) and the object retrieval." +
                                    "A different object was retrieved and its type is not the one we expected." +
                                    $"Expected Type: {typeName}, Actual Type: {finalTypeName}");


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

                GC.Collect();
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
                                Type = objType,
                                HashCode = hashCode,
                                // No need to mask the Method Table in the .NET diver
                                XoredMethodTable = clrObj.Type.MethodTable,
                                XorMask = 0
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


        #region Ping Handler

        private string MakePingResponse(ScubaDiverMessage arg)
        {
            return "{\"status\":\"pong\"}";
        }

        #endregion

        protected override string MakeDomainsResponse(ScubaDiverMessage req)
        {
            List<DomainsDump.AvailableDomain> available = new();
            lock (_clrMdLock)
            {
                foreach (ClrAppDomain clrAppDomain in _runtime.AppDomains)
                {
                    var modules = clrAppDomain.Modules
                        .Select(m => Path.GetFileNameWithoutExtension(m.Name))
                        .Where(m => !string.IsNullOrWhiteSpace(m))
                        .ToList();
                    var dom = new DomainsDump.AvailableDomain()
                    {
                        Name = clrAppDomain.Name,
                        AvailableModules = modules
                    };
                    available.Add(dom);
                }
            }

            DomainsDump dd = new()
            {
                Current = AppDomain.CurrentDomain.FriendlyName,
                AvailableDomains = available
            };

            return JsonConvert.SerializeObject(dd);
        }
        protected override string MakeTypesResponse(ScubaDiverMessage req)
        {
            string assembly = req.QueryString.Get("assembly");

            // Try exact match assembly 
            var allAssembliesInApp = _unifiedAppDomain.GetAssemblies();
            List<Assembly> matchingAssemblies = allAssembliesInApp.Where(assm => assm.GetName().Name == assembly).ToList();
            if (matchingAssemblies.Count == 0)
            {
                // No exact matches, widen search to any assembly *containing* the query
                matchingAssemblies = allAssembliesInApp.Where(module =>
                {
                    if (assembly == null)
                        return false;
                    try
                    {
                        return module?.GetName()?.Name?.Contains(assembly) == true;
                    }
                    catch { }

                    return false;
                }).ToList();
            }

            if (!matchingAssemblies.Any())
            {
                // No matching assemblies found
                return QuickError($"No assemblies found matching the query '{assembly}'");
            }
            else if (matchingAssemblies.Count > 1)
            {
                return $"{{\"error\":\"Too many assemblies found matching the query '{assembly}'. Expected: 1, Got: {matchingAssemblies.Count}\"}}";
            }

            // Got here - we have a single matching assembly.
            Assembly matchingAssembly = matchingAssemblies.Single();


            List<TypesDump.TypeIdentifiers> types = new List<TypesDump.TypeIdentifiers>();
            foreach (Type type in matchingAssembly.GetTypes())
            {
                types.Add(new TypesDump.TypeIdentifiers()
                {
                    TypeName = type.FullName
                });
            }

            TypesDump dump = new()
            {
                AssemblyName = assembly,
                Types = types
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
            //Logger.Debug($"[DotNetDiver] Trying to dump Type: {type}");
            if (assembly != null)
            {
                //Logger.Debug($"[DotNetDiver] Trying to dump Type: {type}, WITH Assembly: {assembly}");
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
        protected override string MakeTypeResponse(ScubaDiverMessage req)
        {
            string body = req.Body;

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
        protected override string MakeHeapResponse(ScubaDiverMessage arg)
        {
            // Since ClrMD works on copied memory (in the snapshot process), we must refresh it so it copies again
            // the updated heap's state between invocations.
            RefreshRuntime();

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

        /// <returns>Unhook action</returns>
        protected override Action HookFunction(FunctionHookRequest request, HarmonyWrapper.HookCallback patchCallback)
        {
            string hookPositionStr = request.HookPosition;
            HarmonyPatchPosition hookPosition = (HarmonyPatchPosition)Enum.Parse(typeof(HarmonyPatchPosition), hookPositionStr);
            if (!Enum.IsDefined(typeof(HarmonyPatchPosition), hookPosition))
                throw new Exception("hook_position has an invalid or unsupported value");

            if (ResolveManagedHookTargetFunc(request, out MethodBase methodInfo, out var resolutionError))
                throw new Exception(resolutionError);

            // Might throw and it's fine
            HarmonyWrapper.Instance.AddHook(methodInfo, hookPosition, patchCallback);

            Action unhookMethod = (Action)(() =>
            {
                HarmonyWrapper.Instance.UnhookAnyHookPosition(methodInfo);
            });
            return unhookMethod;
        }

        private bool ResolveManagedHookTargetFunc(FunctionHookRequest request, out MethodBase methodInfo, out string error)
        {
            error = null;
            methodInfo = null;
            Type resolvedType;
            lock (_clrMdLock)
            {
                resolvedType = _unifiedAppDomain.ResolveType(request.TypeFullName);
            }

            if (resolvedType == null)
            {
                error = "Failed to resolve type";
                return true;
            }

            Type[] paramTypes;
            lock (_clrMdLock)
            {
                paramTypes = request.ParametersTypeFullNames.Select(typeFullName => _unifiedAppDomain.ResolveType(typeFullName))
                    .ToArray();
            }

            // We might be searching for a constructor. Switch based on method name.
            string methodName = request.MethodName;
            methodInfo = methodName == ".ctor"
                ? resolvedType.GetConstructor(paramTypes)
                : resolvedType.GetMethodRecursive(methodName, paramTypes);

            if (methodInfo == null)
            {
                string parametersList = string.Join(", ", request.ParametersTypeFullNames);
                error = $"Failed to find method {methodName}({parametersList}) in type {resolvedType.Name}";
                return true;
            }

            Logger.Debug("[DotNetDiver] Hook Method - Resolved Method");
            return false;
        }

        private string MakeEventUnsubscribeResponse(ScubaDiverMessage arg)
        {
            string tokenStr = arg.QueryString.Get("token");
            if (tokenStr == null || !int.TryParse(tokenStr, out int token))
            {
                return QuickError("Missing parameter 'address'");
            }
            Logger.Debug($"[DotNetDiver][MakeEventUnsubscribeResponse] Called! Token: {token}");

            if (_remoteEventHandler.TryRemove(token, out RegisteredEventHandlerInfo eventInfo))
            {
                eventInfo.EventInfo.RemoveEventHandler(eventInfo.Target, eventInfo.RegisteredProxy);
                return "{\"status\":\"OK\"}";
            }
            return QuickError("Unknown token for event callback subscription");
        }
        protected string MakeEventSubscribeResponse(ScubaDiverMessage arg)
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
            Logger.Debug($"[DotNetDiver][Debug](RegisterEventHandler) objAddrStr={objAddr:X16}");

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

            Logger.Debug($"[DotNetDiver] Adding event handler to event {eventName}...");
            eventObj.AddEventHandler(target, eventHandler);
            Logger.Debug($"[DotNetDiver] Added event handler to event {eventName}!");


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

        /// <summary>
        /// 
        /// </summary>
        /// <param name="callbacksEndpoint"></param>
        /// <param name="token"></param>
        /// <param name="stackTrace"></param>
        /// <param name="parameters"></param>
        /// <returns>Any results returned from the</returns>
        protected override ObjectOrRemoteAddress InvokeControllerCallback(IPEndPoint callbacksEndpoint, int token, string stackTrace, object retValue, params object[] parameters)
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
                    // Check if the object was pinned
                    if (!_freezer.TryGetPinningAddress(parameter, out ulong addr))
                    {
                        // Pin and mark for unpinning later
                        addr = _freezer.Pin(parameter);
                    }

                    remoteParams[i] = ObjectOrRemoteAddress.FromToken(addr, parameter.GetType().FullName);
                }
            }

            ObjectOrRemoteAddress remoteRetVal;
            if (retValue == null)
            {
                remoteRetVal = ObjectOrRemoteAddress.Null;
            }
            else if (retValue.GetType().IsPrimitiveEtc())
            {
                remoteRetVal = ObjectOrRemoteAddress.FromObj(retValue);
            }
            else // Not primitive
            {
                // Check if the object was pinned
                if (!_freezer.TryGetPinningAddress(retValue, out ulong addr))
                {
                    // Pin and mark for unpinning later
                    addr = _freezer.Pin(retValue);
                }
                remoteRetVal = ObjectOrRemoteAddress.FromToken(addr, retValue.GetType().FullName);
            }
            
            
            // Call callback at controller
            InvocationResults hookCallbackResults = reverseCommunicator.InvokeCallback(token, stackTrace, Thread.CurrentThread.ManagedThreadId, remoteRetVal, remoteParams);

            return hookCallbackResults.ReturnedObjectOrAddress;
        }

        #endregion
        protected override string MakeObjectResponse(ScubaDiverMessage arg)
        {
            string objAddrStr = arg.QueryString.Get("address");
            string typeName = arg.QueryString.Get("type_name");
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
                (object instance, ulong pinnedAddress) = GetObject(objAddr, pinningRequested, typeName, hashCodeFallback ? userHashcode : null);
                ObjectDump od = ObjectDumpFactory.Create(instance, objAddr, pinnedAddress);
                return JsonConvert.SerializeObject(od);
            }
            catch (Exception e)
            {
                return QuickError("Failed Getting the object for the user. Error: " + e.Message);
            }
        }
        protected override string MakeCreateObjectResponse(ScubaDiverMessage arg)
        {
            Logger.Debug("[DotNetDiver] Got /create_object request!");
            if (string.IsNullOrEmpty(arg.Body))
                return QuickError("Missing body");
            var request = JsonConvert.DeserializeObject<CtorInvocationRequest>(arg.Body);
            if (request == null)
                return QuickError("Failed to deserialize body");

            Type t = null;
            lock (_clrMdLock)
            {
                t = _unifiedAppDomain.ResolveType(request.TypeFullName);
            }
            if (t == null)
                return QuickError("Failed to resolve type");

            List<object> paramsList = new();
            if (request.Parameters.Any())
            {
                Logger.Debug($"[DotNetDiver] Ctor'ing with parameters. Count: {request.Parameters.Count}");
                paramsList = request.Parameters.Select(ParseParameterObject).ToList();
            }
            else
            {
                // No parameters.
                Logger.Debug("[DotNetDiver] Ctor'ing without parameters");
            }

            object createdObject;
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
                return QuickError("Activator.CreateInstance returned null");

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

        protected override string MakeInvokeResponse(ScubaDiverMessage arg)
        {
            Logger.Debug("[DotNetDiver] Got /Invoke request!");
            string body = arg.Body;

            if (string.IsNullOrEmpty(body))
                return QuickError("Missing body");

            var request = JsonConvert.DeserializeObject<InvocationRequest>(body);
            if (request == null)
                return QuickError("Failed to deserialize body");

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
                Logger.Debug($"[DotNetDiver] Invoking with parameters. Count: {request.Parameters.Count}");
                paramsList = request.Parameters.Select(ParseParameterObject).ToList();
            }
            else
            {
                // No parameters.
                Logger.Debug("[DotNetDiver] Invoking without parameters");
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
                Logger.Debug($"[DotNetDiver] Failed to Resolved method {request.MethodName} in type {dumpedObjType.Name} :/");
                return QuickError("Couldn't find method in type.");
            }

            string argsSummary = string.Join(", ", argumentTypes.Select(arg => arg.Name));
            Logger.Debug($"[DotNetDiver] Resolved method: {method.Name}({argsSummary}), Containing Type: {method.DeclaringType}");

            object results = null;
            try
            {
                argsSummary = string.Join(", ", paramsList.Select(param => param?.ToString() ?? "null"));
                if (string.IsNullOrEmpty(argsSummary))
                    argsSummary = "No Arguments";
                Logger.Debug($"[DotNetDiver] Invoking {method.Name} with those args (Count: {paramsList.Count}): `{argsSummary}`");
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
        protected override string MakeGetFieldResponse(ScubaDiverMessage arg)
        {
            Logger.Debug("[DotNetDiver] Got /get_field request!");
            string body = arg.Body;

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
                    Logger.Debug($"[DotNetDiver] Failed to Resolved field :/");
                    return QuickError("Couldn't find field in type.");
                }

                Logger.Debug($"[DotNetDiver] Resolved field: {fieldInfo.Name}, Containing Type: {fieldInfo.DeclaringType}");

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
        protected override string MakeSetFieldResponse(ScubaDiverMessage arg)
        {
            Logger.Debug("[DotNetDiver] Got /set_field request!");
            string body = arg.Body;

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
                Logger.Debug($"[DotNetDiver] Failed to Resolved field :/");
                return QuickError("Couldn't find field in type.");
            }
            Logger.Debug($"[DotNetDiver] Resolved field: {fieldInfo.Name}, Containing Type: {fieldInfo.DeclaringType}");

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
        protected override string MakeArrayItemResponse(ScubaDiverMessage arg)
        {
            string body = arg.Body;

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
                Logger.Debug("[DotNetDiver] Array access: Object is an IDICTIONARY!");
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
                Logger.Debug("[DotNetDiver] Array access: Object isn't an Array, IList, IDictionary or IEnumerable");
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
        protected override string MakeUnpinResponse(ScubaDiverMessage arg)
        {
            string objAddrStr = arg.QueryString.Get("address");
            if (objAddrStr == null || !ulong.TryParse(objAddrStr, out var objAddr))
            {
                return QuickError("Missing parameter 'address'");
            }
            Logger.Debug($"[DotNetDiver][Debug](Unpin) objAddrStr={objAddr:X16}");

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

        // IDisposable
        public override void Dispose()
        {
            base.Dispose();

            Logger.Debug("[DotNetDiver] Stopping Callback Endpoints Monitor");
            _monitorEndpoints = false;
            try
            {
                endpointsMonitor.Wait();
            }
            catch
            {
                // IDC
            }

            Logger.Debug("[DotNetDiver] Closing ClrMD runtime and snapshot");
            lock (_clrMdLock)
            {
                _runtime?.Dispose();
                _runtime = null;
                _dt?.Dispose();
                _dt = null;
            }

            Logger.Debug("[DotNetDiver] Unpinning objects");
            _freezer.UnpinAll();
            Logger.Debug("[DotNetDiver] Unpinning finished");

            Logger.Debug("[DotNetDiver] Dispatcher returned, Start is complete.");

            Logger.Debug("[DotNetDiver] Removing all event subscriptions & hooks");
            foreach (RegisteredEventHandlerInfo rehi in _remoteEventHandler.Values)
            {
                rehi.EventInfo.RemoveEventHandler(rehi.Target, rehi.RegisteredProxy);
            }
            foreach (RegisteredManagedMethodHookInfo rmhi in _remoteHooks.Values)
            {
                rmhi.UnhookAction();
            }
            _remoteEventHandler.Clear();
            _remoteHooks.Clear();
            Logger.Debug("[DotNetDiver] Removed all event subscriptions & hooks");
        }

    }
}