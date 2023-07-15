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
using System.Threading;
using ScubaDiver.API.Interactions;
using ScubaDiver.API;
using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using ScubaDiver.API.Hooking;
using ScubaDiver.API.Interactions.Callbacks;
using ScubaDiver.Hooking;
using ModuleInfo = Microsoft.Diagnostics.Runtime.ModuleInfo;
using TypeInfo = System.Reflection.TypeInfo;

namespace ScubaDiver
{
    public class MsvcDiver : DiverBase
    {
        private Dictionary<ModuleInfo, TypeInfo[]> scannedModules = new Dictionary<ModuleInfo, TypeInfo[]>();

        public MsvcDiver(IRequestsListener listener) : base(listener)
        {
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
                        if (method is UndecoratedExport undecExport)
                            allExports.Remove(undecExport.Export);
                    }
                }

                // This list should now hold only typeless funcs
                foreach (DllExport export in allExports)
                {
                    // TODO: Then why the fuck am I trying to undecorate??
                    if (!export.TryUndecorate(moduleInfo, out UndecoratedFunction output))
                        continue;

                    module.AddTypelessFunction(export.Name, output);
                }

                // 'operator new' are most likely not exported. We need the trickster to tell us where they are.
                if (_trickster.OperatorNewFuncs.TryGetValue(moduleInfo, out nuint[] operatorNewAddresses))
                {
                    foreach (nuint operatorNewAddr in operatorNewAddresses)
                    {
                        UndecoratedFunction undecFunction =
                            new UndecoratedInternalFunction(
                                undecoratedName: "operator new", 
                                undecoratedFullName: "operator new", 
                                decoratedName: "operator new", 
                                (long)operatorNewAddr, 1,
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
        }

        protected override Action HookFunction(FunctionHookRequest request, HarmonyWrapper.HookCallback patchCallback)
        {
            string rawTypeFilter = request.TypeFullName;
            string methodName = request.MethodName;

            ParseFullTypeName(rawTypeFilter, out var rawAssemblyFilter, out rawTypeFilter);
            var modulesToTypes = GetTypeInfos(rawAssemblyFilter, rawTypeFilter);
            if (modulesToTypes.Count != 1)
                QuickError($"Expected exactly 1 match for module, got {modulesToTypes.Count}");

            Rtti.ModuleInfo module = modulesToTypes.Keys.Single();
            Rtti.TypeInfo[] typeInfos = modulesToTypes[module].ToArray();
            if (typeInfos.Length != 1)
                QuickError($"Expected exactly 1 match for type, got {typeInfos.Length}");

            Rtti.TypeInfo typeInfo = typeInfos.Single();
            List<UndecoratedFunction> methods = GetExportedTypeMethod(module, typeInfo.Name).ToList();
            UndecoratedFunction methodToHook = null;
            foreach (var method in methods)
            {
                if (method.UndecoratedName != methodName)
                    continue;

                if (methodToHook != null)
                    throw new Exception($"Too many matches for {methodName}");

                methodToHook = method;
            }
            if (methodToHook == null)
                throw new Exception($"No matches for {methodName}");
            Logger.Debug("[MsvcDiver] Hook Method - Resolved Method");


            Logger.Debug($"[MsvcDiver] Hooking function {methodName}...");
            DetoursNetWrapper.HookCallback hook =
                (DetoursMethodGenerator.DetoursTrampoline tramp, object[] args, out nuint value) =>
                {
                    object self = new NativeObject((nuint)args.FirstOrDefault(), typeInfo);

                    // TODO: Make the arguments into "NativeObject"s as well? Need to figure out which nuints
                    // are pointers and which aren't...
                    object[] argsToForward = args;
                    if (args.Length > 0)
                        argsToForward = args.Skip(1).ToArray();

                    // TODO: We're currently ignoring the "skip original" return value because the callback
                    // doesn't support setting the return value...
                    patchCallback(self, argsToForward);

                    value = 0;
                    bool skipOriginal = false;
                    return skipOriginal;
                };
            DetoursNetWrapper.Instance.AddHook(methodToHook, hook);
            Logger.Debug($"[MsvcDiver] Hooked function {methodName}!");

            Action unhook = (Action)(() =>
            {
                DetoursNetWrapper.Instance.RemoveHook(methodToHook, hook);
            });
            return unhook;
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="callbacksEndpoint"></param>
        /// <param name="token"></param>
        /// <param name="stackTrace"></param>
        /// <param name="parameters"></param>
        /// <returns>Any results returned from the</returns>
        protected override ObjectOrRemoteAddress InvokeControllerCallback(IPEndPoint callbacksEndpoint, int token, string stackTrace, params object[] parameters)
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
                else if (parameter is NativeObject nativeObj)
                {
                    // TODO: Freeze?
                    remoteParams[i] = ObjectOrRemoteAddress.FromToken(nativeObj.Address, nativeObj.TypeInfo.FullTypeName);
                }
                else if (parameter is object[] arrayParam && arrayParam.All(item => item is nuint))
                {
                    nuint[] pointers = arrayParam.Cast<nuint>().ToArray();
                    remoteParams[i] = ObjectOrRemoteAddress.FromObj(pointers);
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
                    string vftableName = $"{typeInfo.Name}::`vftable'"; // Constructing NameSpace::ClassName::ClassName

                    List<ManagedTypeDump.TypeField> fields = new();
                    List<ManagedTypeDump.TypeMethod> methods = new();
                    List<ManagedTypeDump.TypeMethod> constructors = new();
                    foreach (UndecoratedFunction dllExport in GetExportedTypeMethod(module, typeInfo.Name))
                    {
                        // TODO: Fields could be exported as well..
                        // we only expected the "vftable" field (not actualy a field...) and methods/ctors right now

                        List<ManagedTypeDump.TypeMethod.MethodParameter> parameters;
                        string[] argTypes = dllExport.ArgTypes;
                        if (argTypes != null)
                        {
                            parameters = argTypes.Select((argType, i) =>
                                new ManagedTypeDump.TypeMethod.MethodParameter()
                                {
                                    FullTypeName = argType,
                                    Name = $"a{i}"
                                }).ToList();
                        }
                        else
                        {
                            if (dllExport.UndecoratedFullName == vftableName)
                            {
                                fields.Add(new ManagedTypeDump.TypeField()
                                {
                                    Name = "vftable",
                                    TypeFullName = dllExport.UndecoratedFullName,
                                    Visibility = "Public"
                                });
                                continue;
                            }
                            // Something went wrong when parsing this method's parameters...
                            Logger.Debug($"[{nameof(GetManagedTypeDump)}] Failed to parse parameters of {dllExport.UndecoratedFullName}");
                            continue;
                        }

                        ManagedTypeDump.TypeMethod method = new()
                        {
                            Name = dllExport.UndecoratedName,
                            MangledName = dllExport.DecoratedName,
                            Parameters = parameters,
                            Visibility = "Public" // Because it's exported
                        };

                        if (dllExport.UndecoratedFullName == ctorName)
                            constructors.Add(method);
                        else
                            methods.Add(method);
                    }

                    ManagedTypeDump recusiveManagedTypeDump = new ManagedTypeDump()
                    {
                        Assembly = moduleName,
                        Type = typeInfo.Name,
                        Methods = methods,
                        Constructors = constructors,
                        Fields = fields
                    };
                    return recusiveManagedTypeDump;
                }
            }

            return null;
        }



        /// <summary>
        /// Get a specific method of a specific type, which is exported from the given module.
        /// </summary>
        protected IEnumerable<UndecoratedFunction> GetExportedTypeMethod(Rtti.ModuleInfo module, string typeFullName)
        {
            string membersPrefix = $"{typeFullName}::";
            IReadOnlyList<DllExport> exports = GetExports(module.Name);

            foreach (DllExport dllExport in exports)
            {
                if (dllExport.TryUndecorate(module, out UndecoratedFunction output) &&
                    output.UndecoratedFullName.StartsWith(membersPrefix))
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
            if (!undType.ContainsKey(func.UndecoratedFullName))
                undType[func.UndecoratedFullName] = new UndecoratedMethodGroup();
            undType[func.UndecoratedFullName].Add(func);
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

    public record NativeObject(nuint Address, ScubaDiver.Rtti.TypeInfo TypeInfo);
}
