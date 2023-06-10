using ScubaDiver.API.Interactions.Dumps;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net;
using ScubaDiver.API.Utils;
using NtApiDotNet.Win32;
using System.IO;
using ScubaDiver.Rtti;
using ScubaDiver.API.Hooking;
using ScubaDiver.API.Interactions.Callbacks;
using ScubaDiver.Hooking;
using System.Threading;
using ScubaDiver.API.Interactions;
using ScubaDiver.API;
using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using ModuleInfo = Microsoft.Diagnostics.Runtime.ModuleInfo;
using TypeInfo = System.Reflection.TypeInfo;
using System.Reflection;
using ScubaDiver.Demangle.Demangle.Core.NativeInterface;

namespace ScubaDiver
{
    public class MsvcDiver : DiverBase
    {
        private Dictionary<ModuleInfo, TypeInfo[]> scannedModules = new Dictionary<ModuleInfo, TypeInfo[]>();

        public MsvcDiver(IRequestsListener listener) : base(listener)
        {
            _remoteHooks = new ConcurrentDictionary<int, RegisteredUnmanagedMethodHookInfo>();

            base._responseBodyCreators["/gc"] = MakeGcResponse;
        }

        public override void Start()
        {
            Logger.Debug("[MsvcDiver] Is logging debugs in release? " + Logger.DebugInRelease.Value);

            // Load or Hijack Newtonsoft.Json
            var nsJson = InitNewtonsoftJson();
            Logger.Debug("[MsvcDiver] Newtonsoft.Json's module path: " + nsJson.Location);

            base.Start();
        }


        private MsvcOffensiveGC gc;

        protected string MakeGcResponse(ScubaDiverMessage req)
        {
            Logger.Debug($"[{nameof(MsvcDiver)}] {nameof(MakeGcResponse)} IN!");

            List<UndecoratedModule> undecModules = GetUndecoratedModules();


            Logger.Debug($"[{nameof(MsvcDiver)}] {nameof(MakeGcResponse)} Init'ing GC");
            try
            {
                gc = new MsvcOffensiveGC();
                gc.Init(undecModules);
            }
            catch (Exception e)
            {
                Logger.Debug($"[{nameof(MsvcDiver)}] {nameof(MakeGcResponse)} Exception: " + e);
            }

            Logger.Debug($"[{nameof(MsvcDiver)}] {nameof(MakeGcResponse)} OUT!");
            return "{\"status\":\"ok\"}";
        }


        private Dictionary<string, UndecoratedModule> _undecModeulesCache = new Dictionary<string, UndecoratedModule>();
        private List<UndecoratedModule> GetUndecoratedModules()
        {
            UndecoratedModule GenerateUndecoratedModule(Rtti.ModuleInfo moduleInfo, Rtti.TypeInfo[] types)
            {
                // Getting all exports, type funcs and typeless
                List<DllExport> allExports = new List<DllExport>(GetExports(moduleInfo.Name));

                UndecoratedModule module = new UndecoratedModule(moduleInfo.Name);
                foreach (Rtti.TypeInfo typeInfo in types)
                {
                    IEnumerable<UndecoratedFunction> methods = GetExportedTypeMethod(moduleInfo, typeInfo.Name);
                    foreach (var method in methods)
                    {
                        module.AddTypeFunction(typeInfo, method);

                        // Removing type funcs from allExports
                        if(method is UndecoratedExport undecExport)
                            allExports.Remove(undecExport.Export);
                    }
                }
                
                // This list should now hold only typeless funcs
                foreach (DllExport export in allExports)
                {
                    if(!export.TryUndecorate(moduleInfo, out UndecoratedFunction output))
                        continue;
                    
                    module.AddTypelessFunction(export.Name, output);
                }

                // 'operator new' are most likely not exported. We need the trickster to tell us where they are.
                if (_trickster.OperatorNewFuncs.TryGetValue(moduleInfo, out nuint[] operatorNewAddresses))
                {
                    foreach (nuint operatorNewAddr in operatorNewAddresses)
                    {
                        UndecoratedFunction undecFunction =
                            new UndecoratedInternalFunction("operator new", "operator new", (long)operatorNewAddr,
                                moduleInfo);
                        module.AddTypelessFunction("operator new", undecFunction);
                    }
                }

                return module;
            }

            RefreshRuntime();
            _trickster.ScanOperatorNewFuncs();
            Dictionary<Rtti.ModuleInfo, Rtti.TypeInfo[]> modulesAndTypes = _trickster.ScannedTypes;

            List<UndecoratedModule> output = new();
            foreach (KeyValuePair<Rtti.ModuleInfo, Rtti.TypeInfo[]> kvp in modulesAndTypes)
            {
                var module = kvp.Key;
                if (!_undecModeulesCache.TryGetValue(module.Name, out UndecoratedModule undecModule))
                {
                    // Generate the undecorated module and save in cache
                    undecModule = GenerateUndecoratedModule(module, kvp.Value);
                    _undecModeulesCache[module.Name] = undecModule;
                }
                // store this module for the output
                output.Add(undecModule);
            }

            return output;
        }

        private List<SafeHandle> _injectedDlls = new List<SafeHandle>();
        protected override string MakeInjectDllResponse(ScubaDiverMessage req)
        {
            string dllPath = req.QueryString.Get("dll_path");
            try
            {
                var handle = Windows.Win32.Kernel32.LoadLibrary(dllPath);

                if (handle.IsInvalid)
                    return "{\"status\":\"dll load failed\"}";

                // We must keep a reference or FreeLibrary will automatically be called the the handle object is destructed
                _injectedDlls.Add(handle as SafeHandle);

                // Must take a new snapshot to see our new module
                RefreshRuntime();

                return "{\"status\":\"dll loaded\"}";
            }
            catch (Exception ex)
            {
                return QuickError(ex.Message, ex.StackTrace);
            }
        }


        protected override string MakeDomainsResponse(ScubaDiverMessage req)
        {
            RefreshRuntime();

            List<DomainsDump.AvailableDomain> available = new();
            var modules = _trickster.ModulesParsed
                .Select(m => m.Name)
                .Where(m => !string.IsNullOrWhiteSpace(m))
                .ToList();
            var dom = new DomainsDump.AvailableDomain()
            {
                Name = "dummy_domain",
                AvailableModules = modules
            };
            available.Add(dom);

            DomainsDump dd = new()
            {
                Current = "dummy_domain",
                AvailableDomains = available
            };

            return JsonConvert.SerializeObject(dd);
        }

        private Trickster _trickster = null;

        protected override void RefreshRuntime()
        {
            Logger.Debug("[MsvcDiver][Trickster] Refreshing runtime!");
            _trickster = new Trickster(Process.GetCurrentProcess());
            _trickster.ScanTypes();
            Logger.Debug($"[MsvcDiver][Trickster] DONE refreshing runtime. Num Modules: {_trickster.ScannedTypes.Count}");
            foreach (Rtti.ModuleInfo moduleInfo in _trickster.ScannedTypes.Keys)
            {
                Logger.Debug(
                    $"[MsvcDiver][Trickster]  → Module: {moduleInfo.Name}");
            }
        }

        protected override string MakeUnhookMethodResponse(ScubaDiverMessage arg)
        {
            throw new NotImplementedException();
        }

        protected override string MakeHookMethodResponse(ScubaDiverMessage arg)
        {
            Logger.Debug("[MsvcDiver] Got Hook Method request!");
            string body = arg.Body;

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
            string rawTypeFilter = request.TypeFullName;
            string methodName = request.MethodName;
            string hookPosition = request.HookPosition;
            HarmonyPatchPosition pos = (HarmonyPatchPosition)Enum.Parse(typeof(HarmonyPatchPosition), hookPosition);
            if (pos != HarmonyPatchPosition.Prefix)
                return QuickError($"hook_position in native apps can only be {HarmonyPatchPosition.Prefix}");

            ParseFullTypeName(rawTypeFilter, out var rawAssemblyFilter, out rawTypeFilter);
            var modulesToTypes = GetTypeInfos(rawAssemblyFilter, rawTypeFilter);
            if (modulesToTypes.Count != 1)
                QuickError($"Expected exactly 1 match for module, got {modulesToTypes.Count}");

            Rtti.ModuleInfo module = modulesToTypes.Keys.Single();
            Rtti.TypeInfo[] typeInfos = modulesToTypes[module].ToArray();
            if (typeInfos.Length != 1)
                QuickError($"Expected exactly 1 match for module, got {typeInfos.Length}");

            Rtti.TypeInfo typeInfo = typeInfos.Single();

            var methods = GetExportedTypeMethod(module, typeInfo.Name);

            UndecoratedFunction methodToHook = null;
            foreach (var method in methods)
            {
                if (method.UndecoratedName != methodName)
                    continue;

                if (methodToHook != null)
                    return QuickError($"Too many matches for {methodName}");

                methodToHook = method;
            }


            if (methodToHook == null)
                return QuickError($"No matches for {methodName}");



            Logger.Debug("[MsvcDiver] Hook Method - Resolved Method");

            // We're all good regarding the signature!
            // assign subscriber unique id
            int token = AssignCallbackToken();
            Logger.Debug($"[MsvcDiver] Hook Method - Assigned Token: {token}");

            // Preparing a proxy method that Harmony will invoke
            HarmonyWrapper.HookCallback patchCallback = (obj, args) =>
            {
                var res = InvokeControllerCallback(endpoint, token, new StackTrace().ToString(), obj, args);
                bool skipOriginal = false;
                if (res != null && !res.IsRemoteAddress)
                {
                    object decodedRes = PrimitivesEncoder.Decode(res);
                    if (decodedRes is bool boolRes)
                        skipOriginal = boolRes;
                }
                return skipOriginal;
            };

            Logger.Debug($"[MsvcDiver] Hooking function {methodName}...");
            try
            {
                //DetoursNetWrapper.Instance.AddHook(methodToHook, pos, patchCallback);
                throw new NotImplementedException("LOL");
            }
            catch (Exception ex)
            {
                // Hooking filed so we cleanup the Hook Info we inserted beforehand 
                _remoteHooks.TryRemove(token, out _);

                Logger.Debug($"[DotNetDiver] Failed to hook func {methodName}. Exception: {ex}");
                return QuickError("Failed insert the hook for the function. HarmonyWrapper.AddHook failed.");
            }
            Logger.Debug($"[DotNetDiver] Hooked func {methodName}!");

            // Keeping all hooking information aside so we can unhook later.
            _remoteHooks[token] = new RegisteredUnmanagedMethodHookInfo()
            {
                Endpoint = endpoint,
                OriginalHookedMethod = methodToHook,
                RegisteredProxy = patchCallback
            };


            EventRegistrationResults erResults = new() { Token = token };
            return JsonConvert.SerializeObject(erResults);
        }
        private readonly ConcurrentDictionary<int, RegisteredUnmanagedMethodHookInfo> _remoteHooks;
        private int _nextAvailableCallbackToken;
        public int AssignCallbackToken() => Interlocked.Increment(ref _nextAvailableCallbackToken);
        /// <summary>
        /// 
        /// </summary>
        /// <param name="callbacksEndpoint"></param>
        /// <param name="token"></param>
        /// <param name="stackTrace"></param>
        /// <param name="parameters"></param>
        /// <returns>Any results returned from the</returns>
        public ObjectOrRemoteAddress InvokeControllerCallback(IPEndPoint callbacksEndpoint, int token, string stackTrace, params object[] parameters)
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
                    Console.WriteLine($"Unexpected non native argument to hooked method. Type: {remoteParams[i].GetType().FullName}");
                    throw new Exception($"Unexpected non native argument to hooked method. Type: {remoteParams[i].GetType().FullName}");
                }
            }

            // Call callback at controller
            InvocationResults hookCallbackResults = reverseCommunicator.InvokeCallback(token, stackTrace, remoteParams);

            return hookCallbackResults.ReturnedObjectOrAddress;
        }







        protected override string MakeTypesResponse(ScubaDiverMessage req)
        {
            if (_trickster == null || !_trickster.ScannedTypes.Any())
            {
                RefreshRuntime();
            }

            string assembly = req.QueryString.Get("assembly");
            List<ScubaDiver.Rtti.ModuleInfo> matchingAssemblies = _trickster.ScannedTypes.Keys.Where(assm => assm.Name == assembly).ToList();
            if (matchingAssemblies.Count == 0)
            {
                // No exact matches, widen search to any assembly *containing* the query
                matchingAssemblies = _trickster.ScannedTypes.Keys.Where(module =>
                {
                    try
                    {
                        return module.Name?.Contains(assembly) == true;
                    }
                    catch { }

                    return false;
                }).ToList();
            }


            var assm = matchingAssemblies.Single();
            List<TypesDump.TypeIdentifiers> types = new List<TypesDump.TypeIdentifiers>();
            foreach (var type in _trickster.ScannedTypes[assm])
            {
                types.Add(new TypesDump.TypeIdentifiers()
                {
                    TypeName = $"{assm.Name}!{type.Name}"
                });
            }

            TypesDump dump = new()
            {
                AssemblyName = assembly,
                Types = types
            };

            return JsonConvert.SerializeObject(dump);
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

        public string MakeTypeResponse(TypeDumpRequest dumpRequest)
        {
            string rawTypeFilter = dumpRequest.TypeFullName;
            if (string.IsNullOrEmpty(rawTypeFilter))
            {
                return QuickError("Missing parameter 'TypeFullName'");
            }

            ParseFullTypeName(rawTypeFilter, out var rawAssemblyFilter, out rawTypeFilter);
            string assembly = dumpRequest.Assembly;
            if (!string.IsNullOrEmpty(assembly))
            {
                rawAssemblyFilter = assembly;
            }

            ManagedTypeDump dump = GetManagedTypeDump(rawAssemblyFilter, rawTypeFilter);
            if (dump != null)
                return JsonConvert.SerializeObject(dump);

            return QuickError("Failed to find type in searched assemblies");
        }

        private ManagedTypeDump GetManagedTypeDump(string rawAssemblyFilter, string rawTypeFilter)
        {
            var typeInfos = GetTypeInfos(rawAssemblyFilter, rawTypeFilter);
            foreach (KeyValuePair<Rtti.ModuleInfo, IEnumerable<Rtti.TypeInfo>> moduleAndTypes in typeInfos)
            {
                Rtti.ModuleInfo module = moduleAndTypes.Key;
                foreach (Rtti.TypeInfo typeInfo in moduleAndTypes.Value)
                {
                    string moduleName = typeInfo.ModuleName;
                    string className = typeInfo.Name.Substring(typeInfo.Name.LastIndexOf("::") + 2);
                    string ctorName = $"{typeInfo.Name}::{className}"; // Constructing NameSpace::ClassName::ClassName

                    List<ManagedTypeDump.TypeMethod> methods = new();
                    List<ManagedTypeDump.TypeMethod> constructors = new();
                    foreach (UndecoratedFunction dllExport in GetExportedTypeMethod(module, typeInfo.Name))
                    {
                        ManagedTypeDump.TypeMethod method = new()
                        {
                            Name = dllExport.UndecoratedName,
                            Visibility = "Public" // Because it's exported
                        };

                        if (dllExport.UndecoratedName == ctorName)
                            constructors.Add(method);
                        else
                            methods.Add(method);
                    }

                    ManagedTypeDump recusiveManagedTypeDump = new ManagedTypeDump()
                    {
                        Assembly = moduleName,
                        Type = typeInfo.Name,
                        Methods = methods,
                        Constructors = constructors
                    };
                    return recusiveManagedTypeDump;
                }
            }

            return null;
        }



        protected IEnumerable<UndecoratedFunction> GetExportedTypeMethod(Rtti.ModuleInfo module, string typeFullName)
        {
            string membersPrefix = $"{typeFullName}::";
            IReadOnlyList<DllExport> exports = GetExports(module.Name);

            foreach (DllExport dllExport in exports)
            {
                if (dllExport.TryUndecorate(module, out UndecoratedFunction output) &&
                    output.UndecoratedName.StartsWith(membersPrefix))
                {
                    yield return output;
                }
            }
        }

        private IReadOnlyDictionary<Rtti.ModuleInfo, IEnumerable<Rtti.TypeInfo>> GetTypeInfos(string rawAssemblyFilter, string rawTypeFilter)
        {
            if (_trickster == null || !_trickster.ScannedTypes.Any())
            {
                RefreshRuntime();
            }

            Predicate<string> assmFilter = Filter.CreatePredicate(rawAssemblyFilter);
            Predicate<string> typeFilter = Filter.CreatePredicate(rawTypeFilter);

            Dictionary<Rtti.ModuleInfo, IEnumerable<Rtti.TypeInfo>> output = new();
            foreach (var kvp in _trickster.ScannedTypes)
            {
                var module = kvp.Key;
                if (!assmFilter(module.Name))
                    continue;

                IEnumerable<ScubaDiver.Rtti.TypeInfo> TypesDumperForModule()
                {
                    ScubaDiver.Rtti.TypeInfo[] typeInfos = kvp.Value;

                    foreach (ScubaDiver.Rtti.TypeInfo typeInfo in typeInfos)
                    {
                        if (!typeFilter(typeInfo.Name))
                            continue;
                        yield return typeInfo;
                    }
                }
                output[module] = TypesDumperForModule();
            }

            return output;
        }

        private Dictionary<string, List<DllExport>> _cache = new Dictionary<string, List<DllExport>>();
        private IReadOnlyList<DllExport> GetExports(string moduleName)
        {
            if (!_cache.ContainsKey(moduleName))
            {
                var lib = SafeLoadLibraryHandle.GetModuleHandle(moduleName);
                _cache[moduleName] = lib.Exports.ToList();
            }
            return _cache[moduleName];
        }


        protected override string MakeHeapResponse(ScubaDiverMessage arg)
        {
            if (_trickster == null || !_trickster.ScannedTypes.Any())
            {
                RefreshRuntime();
            }

            if (_trickster.Regions == null || !_trickster.Regions.Any())
            {
                Console.WriteLine("[MsvcDiver] Calling Read Regions in trickster.");
                _trickster.ReadRegions();
                Console.WriteLine("[MsvcDiver] Calling Read Regions in trickster -- done!");
            }

            string rawFilter = arg.QueryString.Get("type_filter");
            ParseFullTypeName(rawFilter, out var rawAssemblyFilter, out var rawTypeFilter);

            Predicate<string> assmFilter = Filter.CreatePredicate(rawAssemblyFilter);
            Predicate<string> typeFilter = Filter.CreatePredicate(rawTypeFilter);

            HeapDump hd = new HeapDump()
            {
                Objects = new List<HeapDump.HeapObject>()
            };
            foreach (var moduleTypesKvp in _trickster.ScannedTypes)
            {
                var module = moduleTypesKvp.Key;
                if (!assmFilter(module.Name))
                    continue;

                IEnumerable<Rtti.TypeInfo> typeInfos = moduleTypesKvp.Value;
                if (!string.IsNullOrEmpty(rawTypeFilter) && rawTypeFilter != "*")
                    typeInfos = typeInfos.Where(ti => typeFilter(ti.Name));


                Logger.Debug($"[{DateTime.Now}] Starting Trickster Scan for {typeInfos.Count()} types");
                Dictionary<Rtti.TypeInfo, IReadOnlyCollection<ulong>> addresses = TricksterUI.Scan(_trickster, typeInfos);
                Logger.Debug($"[{DateTime.Now}] Trickster Scan finished with {addresses.SelectMany(kvp => kvp.Value).Count()} results");
                foreach (var typeInstancesKvp in addresses)
                {
                    Rtti.TypeInfo typeInfo = typeInstancesKvp.Key;
                    foreach (nuint addr in typeInstancesKvp.Value)
                    {
                        HeapDump.HeapObject ho = new HeapDump.HeapObject()
                        {
                            Address = addr,
                            MethodTable = typeInfo.Address,
                            Type = $"{module.Name}!{typeInfo.Name}"
                        };
                        hd.Objects.Add(ho);
                    }
                }
            }

            return JsonConvert.SerializeObject(hd);

        }

        private static void ParseFullTypeName(string rawFilter, out string rawAssemblyFilter, out string rawTypeFilter)
        {
            rawAssemblyFilter = "*";
            rawTypeFilter = rawFilter;
            if (rawFilter.Contains('!'))
            {
                var splitted = rawFilter.Split('!');
                rawAssemblyFilter = splitted[0];
                rawTypeFilter = splitted[1];
            }
        }

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

            ParseFullTypeName(typeName, out var rawAssemblyFilter, out var rawTypeFilter);

            try
            {
                var moduleAndTypes = GetTypeInfos(rawAssemblyFilter, rawTypeFilter).Single();
                Rtti.TypeInfo typeInfo = moduleAndTypes.Value.Single();

                // TODO: Actual Pin
                ulong pinAddr = 0x0;

                ObjectDump od = new ObjectDump()
                {
                    Type = $"{typeInfo.ModuleName}!{typeInfo.Name}",
                    RetrivalAddress = objAddr,
                    PinnedAddress = pinAddr,
                    HashCode = 0x0bad0bad
                };
                return JsonConvert.SerializeObject(od);
            }
            catch (Exception e)
            {
                return QuickError("Failed Getting the object for the user. Error: " + e.Message);
            }
        }

        protected override string MakeCreateObjectResponse(ScubaDiverMessage arg)
        {
            return QuickError("Not Implemented");
        }

        protected override string MakeInvokeResponse(ScubaDiverMessage arg)
        {
            return QuickError("Not Implemented");
        }

        protected override string MakeGetFieldResponse(ScubaDiverMessage arg)
        {
            return QuickError("Not Implemented");
        }

        protected override string MakeSetFieldResponse(ScubaDiverMessage arg)
        {
            return QuickError("Not Implemented");
        }

        protected override string MakeArrayItemResponse(ScubaDiverMessage arg)
        {
            return QuickError("Not Implemented");
        }

        protected override string MakeUnpinResponse(ScubaDiverMessage arg)
        {
            return QuickError("Not Implemented");
        }

        public override void Dispose()
        {
        }

    }

    public class UndecoratedMethodGroup : List<UndecoratedFunction>
    {
    }

    public class UndecoratedType : Dictionary<string, UndecoratedMethodGroup>
    {
        public void AddOrCreate(string methodName, UndecoratedFunction func) => GetOrAddGroup(methodName).Add(func);

        private UndecoratedMethodGroup GetOrAddGroup(string method)
        {
            if (!this.ContainsKey(method))
                this[method] = new UndecoratedMethodGroup();
            return this[method];
        }
    }

    public class UndecoratedModule
    {
        public string Name { get; private set; }

        private Dictionary<Rtti.TypeInfo, UndecoratedType> _types;
        private Dictionary<string, UndecoratedMethodGroup> _typelessFunctions;

        public UndecoratedModule(string name)
        {
            Name = name;
            _types = new Dictionary<Rtti.TypeInfo, UndecoratedType>();
            _typelessFunctions = new Dictionary<string, UndecoratedMethodGroup>();
        }

        public IEnumerable<Rtti.TypeInfo> Type => _types.Keys;

        public bool TryGetType(Rtti.TypeInfo type, out UndecoratedType res)
            => _types.TryGetValue(type, out res);


        public void AddTypeFunction(Rtti.TypeInfo type, UndecoratedFunction func)
        {
            var undType = GetOrAdd(type);
            if (!undType.ContainsKey(func.UndecoratedName))
                undType[func.UndecoratedName] = new UndecoratedMethodGroup();
            undType[func.UndecoratedName].Add(func);
        }

        public bool TryGetTypeFunc(Rtti.TypeInfo type, string undecMethodName, out UndecoratedMethodGroup res)
        {
            res = null;
            return TryGetType(type, out var undType) && undType.TryGetValue(undecMethodName, out res);
        }
        public void AddTypelessFunction(string decoratedMethodName, UndecoratedFunction func)
        {
            if (!_typelessFunctions.ContainsKey(decoratedMethodName))
                _typelessFunctions[decoratedMethodName] = new UndecoratedMethodGroup();
            _typelessFunctions[decoratedMethodName].Add(func);
        }
        public bool TryGetTypelessFunc(string decoratedMethodName, out UndecoratedMethodGroup res)
        {
            return _typelessFunctions.TryGetValue(decoratedMethodName, out res);
        }

        private UndecoratedType GetOrAdd(Rtti.TypeInfo type)
        {
            if (!_types.ContainsKey(type))
                _types[type] = new UndecoratedType();
            return _types[type];
        }
    }
}
