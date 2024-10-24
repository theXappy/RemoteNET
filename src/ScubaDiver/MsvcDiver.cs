using ScubaDiver.API.Interactions.Dumps;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net;
using ScubaDiver.API.Utils;
using ScubaDiver.Rtti;
using ScubaDiver.API.Interactions;
using ScubaDiver.API;
using System.Runtime.InteropServices;
using System.Threading;
using ScubaDiver.API.Interactions.Callbacks;
using ScubaDiver.Hooking;
using Windows.Win32.Foundation;
using ScubaDiver.API.Hooking;
using TypeInfo = ScubaDiver.Rtti.TypeInfo;

namespace ScubaDiver
{
    public class MsvcDiver : DiverBase
    {
        private TricksterWrapper _tricksterWrapper = null;
        private IReadOnlyExportsMaster _exportsMaster = null;

        public MsvcDiver(IRequestsListener listener) : base(listener)
        {
            _responseBodyCreators["/gc"] = MakeGcHookModuleResponse;
            _responseBodyCreators["/gc_stats"] = MakeGcStatsResponse;

            _tricksterWrapper = new TricksterWrapper();
            _exportsMaster = _tricksterWrapper.ExportsMaster;
        }

        public override void Start()
        {
            Logger.Debug("[MsvcDiver] Is logging debugs in release? " + Logger.DebugInRelease.Value);

            // Load or Hijack Newtonsoft.Json
            var nsJson = InitNewtonsoftJson();
            Logger.Debug("[MsvcDiver] Newtonsoft.Json's module path: " + nsJson.Location);

            base.Start();
        }


        private MsvcOffensiveGC _offensiveGC = null;
        private MsvcFrozenItemsCollection _freezer = null;

        protected string MakeGcHookModuleResponse(ScubaDiverMessage req)
        {
            string assemblyFilter = req.QueryString.Get("assembly");
            if (assemblyFilter == null || assemblyFilter == "*")
                return QuickError("'assembly' parameter can't be null or wildcard.");

            Predicate<string> assemblyFilterPredicate = Filter.CreatePredicate(assemblyFilter);

            Logger.Debug($"[{nameof(MsvcDiver)}] {nameof(MakeGcHookModuleResponse)} IN!");
            if (_offensiveGC == null)
            {
                if (_tricksterWrapper.RefreshRequired())
                    _tricksterWrapper.Refresh();
                _offensiveGC = new MsvcOffensiveGC();
                _freezer = new MsvcFrozenItemsCollection(_offensiveGC);
            }

            List<UndecoratedModule> undecoratedModules = _tricksterWrapper.GetUndecoratedModules(assemblyFilterPredicate);
            Logger.Debug($"[{nameof(MsvcDiver)}] {nameof(MakeGcHookModuleResponse)} Initialization GC");
            try
            {
                _offensiveGC.HookModules(undecoratedModules);
                foreach(UndecoratedModule module in undecoratedModules)
                {
                    _offensiveGC.HookAllFreeFuncs(module, _tricksterWrapper.GetUndecoratedModules());
                }
            }
            catch (Exception e)
            {
                Logger.Debug($"[{nameof(MsvcDiver)}] {nameof(MakeGcHookModuleResponse)} Exception: " + e);
            }

            Logger.Debug($"[{nameof(MsvcDiver)}] {nameof(MakeGcHookModuleResponse)} OUT!");
            return "{\"status\":\"ok\"}";
        }
        protected string MakeGcStatsResponse(ScubaDiverMessage req)
        {
            Dictionary<string, object> output = new Dictionary<string, object>();
            output["ClassInstances"] = _offensiveGC.ClassInstances;
            output["ClassSizes"] = _offensiveGC.ClassSizes;
            output["AddressesSizes"] = _offensiveGC.AddressesSizes;
            return JsonConvert.SerializeObject(output);
        }

        private List<SafeHandle> _injectedDlls = new();
        protected override string MakeInjectDllResponse(ScubaDiverMessage req)
        {
            string dllPath = req.QueryString.Get("dll_path");
            try
            {
                var handle = Windows.Win32.PInvoke.LoadLibrary(dllPath);

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
            var modules = _tricksterWrapper.GetModules();
            var moduleNames = modules
                .Select(m => m.Name)
                .Where(m => !string.IsNullOrWhiteSpace(m))
                .ToList();
            var dom = new DomainsDump.AvailableDomain()
            {
                Name = "dummy_domain",
                AvailableModules = moduleNames
            };
            available.Add(dom);

            DomainsDump dd = new()
            {
                Current = "dummy_domain",
                AvailableDomains = available
            };

            return JsonConvert.SerializeObject(dd);
        }


        protected override void RefreshRuntime() => RefreshRuntimeInternal(false);

        protected void RefreshRuntimeInternal(bool force)
        {
            Logger.Debug($"[{DateTime.Now}][MsvcDiver][RefreshRuntime] Refreshing runtime! forced?");
            if (!_tricksterWrapper.RefreshRequired() && !force)
            {
                Logger.Debug($"[{DateTime.Now}][MsvcDiver][RefreshRuntime] Refreshing avoided...");
                return;
            }
            _tricksterWrapper.Refresh();
            Logger.Debug($"[{DateTime.Now}][MsvcDiver][RefreshRuntime] DONE refreshing runtime. Num Modules: {_tricksterWrapper.GetModules().Count}");
        }

        protected override Action HookFunction(FunctionHookRequest request, HarmonyWrapper.HookCallback patchCallback)
        {
            string rawTypeFilter = request.TypeFullName;
            string methodName = request.MethodName;

            ParseFullTypeName(rawTypeFilter, out var rawAssemblyFilter, out rawTypeFilter);
            var modulesToTypes = _tricksterWrapper.SearchTypes(rawAssemblyFilter, rawTypeFilter);
            if (modulesToTypes.Count != 1)
                QuickError($"Expected exactly 1 match for module, got {modulesToTypes.Count}");

            ModuleInfo module = modulesToTypes.Keys.Single();
            Rtti.TypeInfo[] typeInfos = modulesToTypes[module].ToArray();
            if (typeInfos.Length != 1)
                QuickError($"Expected exactly 1 match for type, got {typeInfos.Length}");


            Rtti.TypeInfo typeInfo;
            try
            {
                typeInfo = typeInfos.Single();
            }
            catch (Exception ex)
            {
                throw new Exception(ex.Message +
                                    $".\n typeInfos: {String.Join(", ", typeInfos.Select(x => x.ToString()))}");
            }

            // Get all exported members of the requseted type
            List<UndecoratedSymbol> members = _exportsMaster.GetExportedTypeMembers(module, typeInfo.Name).ToList();

            // Find the first vftable within all members
            // (TODO: Bug? How can I tell this is the "main" vftable?)
            UndecoratedSymbol vftable = members.FirstOrDefault(member => member.UndecoratedName.EndsWith("`vftable'"));

            string hookPositionStr = request.HookPosition;
            HarmonyPatchPosition hookPosition = (HarmonyPatchPosition)Enum.Parse(typeof(HarmonyPatchPosition), hookPositionStr);
            if (!Enum.IsDefined(typeof(HarmonyPatchPosition), hookPosition))
                throw new Exception("hook_position has an invalid or unsupported value");

            List<UndecoratedFunction> exportedFuncs = members.OfType<UndecoratedFunction>().ToList();
            List<UndecoratedFunction> virtualFuncs = new List<UndecoratedFunction>();
            if (vftable != null)
            {

                virtualFuncs = VftableParser.AnalyzeVftable(_tricksterWrapper.GetProcessHandle(), 
                    module, 
                    _exportsMaster.GetUndecoratedExports(module).ToList(), 
                    vftable.Address);

                // Remove duplicates - the methods which are both virtual and exported.
                virtualFuncs = virtualFuncs.Where(method => !exportedFuncs.Contains(method)).ToList();
            }
            var allFuncs = exportedFuncs.Concat(virtualFuncs);

            // Find all methods with the requested name
            var overloads = allFuncs.Where(method => method.UndecoratedName == methodName);
            // Find the specific overload with the right argument types
            UndecoratedFunction methodToHook = overloads.SingleOrDefault(method =>
                method.ArgTypes.Skip(1).SequenceEqual(request.ParametersTypeFullNames));

            if (methodToHook == null)
            {
                throw new Exception($"No matches for {methodName} in type {typeInfo}");
            }

            Logger.Debug("[MsvcDiver] Hook Method - Resolved Method");
            Logger.Debug($"[MsvcDiver] Hooking function {methodName}...");



            // TODO: Is "nuint" return type always right here?
            DetoursNetWrapper.Instance.AddHook(typeInfo, methodToHook, patchCallback, hookPosition);
            Logger.Debug($"[MsvcDiver] Hooked function {methodName}!");

            Action unhook = (Action)(() =>
            {
                DetoursNetWrapper.Instance.RemoveHook(methodToHook, patchCallback);
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
                else if (parameter is NativeObject nativeObj)
                {
                    // TODO: Freeze?
                    remoteParams[i] = ObjectOrRemoteAddress.FromToken(nativeObj.Address, nativeObj.TypeInfo.FullTypeName);
                }
                else if (parameter is CharStar charStar)
                {
                    // TODO: Freeze?
                    var oora = ObjectOrRemoteAddress.FromToken(charStar.Address, typeof(CharStar).FullName);
                    oora.EncodedObject = charStar.Value;
                    remoteParams[i] = oora;
                }
                else // Not primitive
                {
                    Logger.Debug($"Unexpected non native argument to hooked method. Type: {remoteParams[i].GetType().FullName}");
                    throw new Exception($"Unexpected non native argument to hooked method. Type: {remoteParams[i].GetType().FullName}");
                }
            }

            ObjectOrRemoteAddress retValueOora;
            if (retValue == null)
            {
                retValueOora = ObjectOrRemoteAddress.Null;
            }
            else if (retValue.GetType().IsPrimitiveEtc())
            {
                retValueOora = ObjectOrRemoteAddress.FromObj(retValue);
            }
            else if (retValue is NativeObject nativeObj)
            {
                // TODO: Freeze?
                retValueOora = ObjectOrRemoteAddress.FromToken(nativeObj.Address, nativeObj.TypeInfo.FullTypeName);
            }
            else if (retValue is CharStar charStar)
            {
                // TODO: Freeze?
                var oora = ObjectOrRemoteAddress.FromToken(charStar.Address, typeof(CharStar).FullName);
                oora.EncodedObject = charStar.Value;
                retValueOora = oora;
            }
            else // Not primitive
            {
                Logger.Debug($"Unexpected non native ret value of hooked method. Type: {retValue.GetType().FullName}");
                throw new Exception($"Unexpected non native argument to hooked method. Type: {retValue.GetType().FullName}");
            }
            

            // Call callback at controller
            InvocationResults hookCallbackResults = reverseCommunicator.InvokeCallback(token, stackTrace, Thread.CurrentThread.ManagedThreadId, retValueOora, remoteParams);

            return hookCallbackResults.ReturnedObjectOrAddress;
        }

        protected override string MakeTypesResponse(ScubaDiverMessage req)
        {
            string assemblyFilter = req.QueryString.Get("assembly");
            Predicate<string> assemblyFilterPredicate = Filter.CreatePredicate(assemblyFilter);

            List<UndecoratedModule> matchingAssemblies = _tricksterWrapper.GetUndecoratedModules(assemblyFilterPredicate).ToList();
            List<TypesDump.TypeIdentifiers> types = new();
            if (matchingAssemblies.Count == 0)
            {
                return QuickError($"No modules matched the filter '{assemblyFilter}'");
            }
            if (matchingAssemblies.Count > 1)
            {
                string assembliesList = string.Join(", ", matchingAssemblies.Select(x => x.Name).ToArray());
                return QuickError(
                    $"Too many modules matched the filter '{assemblyFilter}'. Found {matchingAssemblies.Count} ({assembliesList})");
            }

            UndecoratedModule module = matchingAssemblies.Single();
            foreach (Rtti.TypeInfo type in module.Types)
            {
                types.Add(new TypesDump.TypeIdentifiers()
                {
                    TypeName = $"{module.Name}!{type.Name}"
                });
            }

            TypesDump dump = new()
            {
                AssemblyName = module.Name,
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

            var request = JsonConvert.DeserializeObject<TypeDumpRequest>(body);
            if (request == null)
            {
                return QuickError("Failed to deserialize body");
            }

            TypeDump dump;
            if (request.MethodTableAddress != 0)
            {
                dump = GetTypeDump((nuint)request.MethodTableAddress);
            }
            else
            {
                dump = GetRttiType(request.TypeFullName, request.Assembly);
            }

            if (dump != null)
                return JsonConvert.SerializeObject(dump);

            return QuickError("Failed to find type in searched assemblies");
        }

        private TypeDump GetTypeDump(nuint methodTableAddress)
        {
            var modules = _tricksterWrapper.GetModules();
            foreach (ModuleInfo module in modules)
            {
                if (module.BaseAddress > methodTableAddress ||
                    methodTableAddress >= (module.BaseAddress + module.Size))
                    continue;

                var types = _tricksterWrapper.SearchTypes(module.Name, "*").Values.SingleOrDefault();
                foreach (TypeInfo typeInfo in types)
                {
                    if (typeInfo is not FirstClassTypeInfo firstClassTypeInfo)
                        continue;
                    if (firstClassTypeInfo.VftableAddress != methodTableAddress)
                        continue;

                    IReadOnlyList<UndecoratedSymbol> exports = _exportsMaster.GetUndecoratedExports(module).ToList();
                    return GenerateTypeDump(typeInfo, module, exports);
                }
            }

            return null;
        }

        private TypeDump GenerateTypeDump(TypeInfo typeInfo, ModuleInfo module, IReadOnlyList<UndecoratedSymbol> exports)
        {
#pragma warning disable CS0168 // Variable is declared but never used
            UndecoratedSymbol vftable;
#pragma warning restore CS0168 // Variable is declared but never used
            List<TypeDump.TypeField> fields = new();
            List<TypeDump.TypeMethod> methods = new();
            List<TypeDump.TypeMethod> constructors = new();
            List<TypeDump.TypeMethodTable> vftables = new();
            DeconstructRttiType(typeInfo, module, fields, constructors, methods, vftables);

            HANDLE process = _tricksterWrapper.GetProcessHandle();
            AnalyzeVftables(vftables, process, module, exports, methods);

            TypeDump recusiveTypeDump = new TypeDump()
            {
                Assembly = module.Name,
                Type = typeInfo.Name,
                Methods = methods,
                Constructors = constructors,
                Fields = fields,
                MethodTables = vftables
            };
            return recusiveTypeDump;
        }

        private TypeDump GetRttiType(string rawTypeFilter, string assembly = null)
        {
            if (string.IsNullOrEmpty(rawTypeFilter))
            {
                throw new Exception("Missing parameter 'TypeFullName'");
            }

            ParseFullTypeName(rawTypeFilter, out var rawAssemblyFilter, out rawTypeFilter);
            if (!string.IsNullOrEmpty(assembly))
            {
                rawAssemblyFilter = assembly;
            }

            TypeDump dump = GetTypeDump(rawAssemblyFilter, rawTypeFilter);
            return dump;
        }

        private TypeDump GetTypeDump(string rawAssemblyFilter, string rawTypeFilter)
        {
            //Logger.Debug($"[GetTypeDump] Querying for rawAssemblyFilter: {rawAssemblyFilter}, rawTypeFilter: {rawTypeFilter}");
            var modulesAndTypes = _tricksterWrapper.SearchTypes(rawAssemblyFilter, rawTypeFilter);

            foreach (KeyValuePair<ModuleInfo, IEnumerable<Rtti.TypeInfo>> moduleAndTypes in modulesAndTypes)
            {
                ModuleInfo module = moduleAndTypes.Key;
                IReadOnlyList<UndecoratedSymbol> exports = _exportsMaster.GetUndecoratedExports(module).ToList();
                foreach (Rtti.TypeInfo typeInfo in moduleAndTypes.Value)
                {
#pragma warning disable CS0168 // Variable is declared but never used
                    UndecoratedSymbol vftable;
#pragma warning restore CS0168 // Variable is declared but never used
                    List<TypeDump.TypeField> fields = new();
                    List<TypeDump.TypeMethod> methods = new();
                    List<TypeDump.TypeMethod> constructors = new();
                    List<TypeDump.TypeMethodTable> vftables = new();
                    DeconstructRttiType(typeInfo, module, fields, constructors, methods, vftables);

                    HANDLE process = _tricksterWrapper.GetProcessHandle();
                    AnalyzeVftables(vftables, process, module, exports, methods);

                    TypeDump recusiveTypeDump = new TypeDump()
                    {
                        Assembly = module.Name,
                        Type = typeInfo.Name,
                        Methods = methods,
                        Constructors = constructors,
                        Fields = fields,
                        MethodTables = vftables
                    };
                    return recusiveTypeDump;
                }
            }

            return null;
        }

        private void DeconstructRttiType(Rtti.TypeInfo typeInfo,
            ModuleInfo module,
            List<TypeDump.TypeField> fields,
            List<TypeDump.TypeMethod> constructors,
            List<TypeDump.TypeMethod> methods,
            List<TypeDump.TypeMethodTable> vftables)
        {
            string className = typeInfo.Name.Substring(typeInfo.Name.LastIndexOf("::") + 2);
            string ctorName = $"{typeInfo.Name}::{className}"; // Constructing NameSpace::ClassName::ClassName
            string vftableName = $"{typeInfo.Name}::`vftable'"; // Constructing NameSpace::ClassName::`vftable
            foreach (UndecoratedSymbol dllExport in _exportsMaster.GetExportedTypeMembers(module, typeInfo.Name))
            {
                if (dllExport is UndecoratedFunction undecoratedFunc)
                {
                    var typeMethod = VftableParser.ConvertToTypeMethod(undecoratedFunc);
                    if (typeMethod == null)
                    {
                        Logger.Debug($"[MsvcDiver] Failed to convert UndecoratedFunction: {undecoratedFunc.UndecoratedFullName}. Skipping.");
                        continue;
                    }

                    if (typeMethod.UndecoratedFullName == ctorName)
                        constructors.Add(typeMethod);
                    else
                        methods.Add(typeMethod);
                }
                else if (dllExport is UndecoratedExportedField undecField)
                {
                    bool isVftable = HandleVftable(undecField, vftableName, fields);
                    if (isVftable)
                    {
                        if (vftables.Count > 0)
                        {
                            var mainVftable = vftables.First();
                            Logger.Debug(
                                $"Secondary vftable export found. Old: {mainVftable.Name} (0x{mainVftable.Address:X16}) , " +
                                $"New: {undecField.UndecoratedFullName} (0x{undecField.Address:X16}) ,");
                        }

                        // Keep vftable aside so we can also gather functions from it
                        vftables.Add(new TypeDump.TypeMethodTable
                        {
                            Name = undecField.DecoratedName,
                            Address = undecField.Address,
                        });
                        continue;
                    }

                    HandleTypeField(undecField, fields);
                }
            }
        }

        private bool HandleVftable(UndecoratedExportedField undecField, string vftableName,
            List<TypeDump.TypeField> fields)
        {
            // vftable gets a special treatment because we need it outside this func.
            if (undecField.UndecoratedFullName != vftableName)
                return false;


            fields.Add(new TypeDump.TypeField()
            {
                Name = "vftable",
                TypeFullName = undecField.UndecoratedFullName,
                Visibility = "Public"
            });

            return true;
        }

        private void HandleTypeField(UndecoratedExportedField undecField,
            List<TypeDump.TypeField> fields)
        {
            fields.Add(new TypeDump.TypeField()
            {
                Name = undecField.UndecoratedName,
                TypeFullName = undecField.UndecoratedFullName,
                Visibility = "Public"
            });
        }

        private void AnalyzeVftables(List<TypeDump.TypeMethodTable> vftables, HANDLE process, ModuleInfo module, IReadOnlyList<UndecoratedSymbol> exports,
            List<TypeDump.TypeMethod> methods)
        {
            foreach (TypeDump.TypeMethodTable vftable in vftables)
            {
                AnalyzeVftable(vftable, process, module, exports, methods);
            }
        }

        private void AnalyzeVftable(TypeDump.TypeMethodTable vftable, HANDLE process, ModuleInfo module, IReadOnlyList<UndecoratedSymbol> exports,
            List<TypeDump.TypeMethod> methods)
        {
            // Parse functions from the vftable
            List<UndecoratedFunction> virtualFunctionsInternal =
                VftableParser.AnalyzeVftable(process, module, exports, vftable.Address);

            // Convert "Undecorated Functions" into "Type Methods"
            IEnumerable<TypeDump.TypeMethod> virtualFunctions = virtualFunctionsInternal
                .Select(VftableParser.ConvertToTypeMethod)
                .Where(x => x != null);

            // Filter out all the existing methods
            foreach (TypeDump.TypeMethod virtualFunction in virtualFunctions)
            {
                bool exists = methods.Any(existingMethod =>
                    existingMethod.UndecoratedFullName == virtualFunction.UndecoratedFullName);
                if (exists)
                    continue;

                methods.Add(virtualFunction);
            }
        }

        private FirstClassTypeInfo ResolveTypeFromVftableAddress(nuint address)
        {
            foreach (var kvp in _tricksterWrapper.GetDecoratedTypes())
            {
                var module = kvp.Key;
                if (address < module.BaseAddress || address > (module.BaseAddress + module.Size))
                    continue;

                var typeInfos = kvp.Value;

                foreach (var typeInfo in typeInfos)
                {
                    if (typeInfo is FirstClassTypeInfo firstClassTypeInfo && firstClassTypeInfo.VftableAddress == address)
                        return firstClassTypeInfo;
                }
                // This module was the module containing our queried address.
                // We couldn't find the type within the module.
                // Modules don't overlap so no other module will help use. It's safe to abort (Skipping the rest of the modules list)
                return null;
            }

            // Address was not in range of any of the modules...
            return null;
        }

        protected override string MakeHeapResponse(ScubaDiverMessage arg)
        {
            // Since trickster works on copied memory, we must refresh it so it copies again
            // the updated heap's state between invocations.
            RefreshRuntimeInternal(true);

            string rawFilter = arg.QueryString.Get("type_filter");
            ParseFullTypeName(rawFilter, out var rawAssemblyFilter, out var rawTypeFilter);

            Predicate<string> assmFilter = Filter.CreatePredicate(rawAssemblyFilter);
            Predicate<string> typeFilter = Filter.CreatePredicate(rawTypeFilter);
            IEnumerable<FirstClassTypeInfo> allClassesToScanFor = Array.Empty<FirstClassTypeInfo>();
            foreach (var undecModule in _tricksterWrapper.GetUndecoratedModules(assmFilter))
            {
                IEnumerable<FirstClassTypeInfo> currModuleClasses =
                    undecModule.Types.OfType<FirstClassTypeInfo>();
                if (!string.IsNullOrEmpty(rawTypeFilter) && rawTypeFilter != "*")
                    currModuleClasses = currModuleClasses.Where(ti => typeFilter(ti.Name));

                Logger.Debug($"[{DateTime.Now}] Trickster aggregating types from {undecModule.Name}");
                allClassesToScanFor = allClassesToScanFor.Concat(currModuleClasses);
            }

            //
            // Heap Search using Trickster
            //
            HeapDump output = new HeapDump();
            Logger.Debug($"[{DateTime.Now}] Starting Trickster Scan for class instances.");
            Dictionary<FirstClassTypeInfo, IReadOnlyCollection<ulong>> addresses = _tricksterWrapper.Scan(allClassesToScanFor);
            Logger.Debug($"[{DateTime.Now}] Trickster Scan finished with {addresses.SelectMany(kvp => kvp.Value).Count()} results");
            foreach (var typeInstancesKvp in addresses)
            {
                FirstClassTypeInfo typeInfo = typeInstancesKvp.Key;
                foreach (nuint address in typeInstancesKvp.Value.Select(l => (nuint)l))
                {
                    HeapDump.HeapObject ho = new HeapDump.HeapObject()
                    {
                        Address = address,
                        XoredMethodTable = typeInfo.XoredVftableAddress,
                        XorMask = FirstClassTypeInfo.XorMask,
                        Type = typeInfo.FullTypeName
                    };
                    output.Objects.Add(ho);
                }
            }

            // Heap & Search using Offensive GC (if enabled)
            if (_offensiveGC != null)
            {
                foreach (var kvp in _offensiveGC.ClassInstances.Where(kvp => assmFilter(kvp.Key)))
                {
                    string module = kvp.Key;
                    foreach (var kvp2 in kvp.Value.Where(kvp2 => typeFilter(kvp2.Key)))
                    {
                        string className = kvp2.Key;
                        foreach (nuint address in kvp2.Value)
                        {
                            HeapDump.HeapObject ho = new HeapDump.HeapObject()
                            {
                                Address = address,
                                Type = $"{module}!{className}"
                            };
                            output.Objects.Add(ho);
                        }
                    }
                }
            }

            return JsonConvert.SerializeObject(output);
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
            if (pinningRequested)
            {
                if (_freezer == null)
                    return QuickError("Pinning requested but Freezer/Offensive GC was not initialized.");
            }

            try
            {
                // Check if the object is already frozen
                if (_freezer.IsFrozen(objAddr))
                {
                    Logger.Debug($"[MsvcDiver][MakeObjectResponse] Object at 0x{objAddr:X16} is already frozen.");
                    ObjectDump od = new ObjectDump()
                    {
                        Type = typeName,
                        RetrivalAddress = objAddr,
                        PinnedAddress = objAddr,
                        HashCode = 0x0bad0bad
                    };
                    return JsonConvert.SerializeObject(od);
                }

                // TODO: Wrong for x86
                long vftable = Marshal.ReadInt64(new IntPtr((long)objAddr));
                Rtti.TypeInfo typeInfo = ResolveTypeFromVftableAddress((nuint)vftable);
                if (typeInfo == null)
                {
                    throw new Exception("Failed to resolve vftable of target to any RTTI type.");
                }

                ulong pinningAddress = objAddr;
                if (pinningRequested)
                {
                    Logger.Debug($"[MsvcDiver][MakeObjectResponse] Pinning object at 0x{objAddr}");
                    pinningAddress = _freezer.Pin(objAddr);
                    Logger.Debug($"[MsvcDiver][MakeObjectResponse] Pinned object at 0x{objAddr}");
                }

                ObjectDump od = new ObjectDump()
                {
                    Type = typeInfo.FullTypeName,
                    RetrivalAddress = objAddr,
                    PinnedAddress = objAddr,
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
            Logger.Debug("[MsvcDiver] Got /Invoke request!");
            string body = arg.Body;
            if (string.IsNullOrEmpty(body))
            {
                return QuickError("Missing body");
            }
            var request = JsonConvert.DeserializeObject<InvocationRequest>(body);
            if (request == null)
            {
                return QuickError("Failed to deserialize body");
            }

            // Need to figure target instance and the target type.
            // In case of a static call the target instance stays null.
            if (request.ObjAddress == 0)
            {
                return QuickError("Calling a instance-less function is not implemented");
            }
            TypeDump dumpedObjType = GetRttiType(request.TypeFullName);
            // Check if we have this objects in our pinned pool
            // TODO: Pull from freezer?
            nuint objAddress = (nuint)request.ObjAddress;

            //
            // We have our target and it's type. Now look for a matching overload for the
            // function to invoke.
            //
            List<object> paramsList = new();
            if (request.Parameters.Any())
            {
                Logger.Debug($"[MsvcDiver] Invoking with parameters. Count: {request.Parameters.Count}");
                paramsList = request.Parameters.Select(ParseParameterObject).ToList();
            }
            else
            {
                // No parameters.
                Logger.Debug("[MsvcDiver] Invoking without parameters");
            }

            // Search the method/ctor with the matching signature
            List<TypeDump.TypeMethod> overloads = dumpedObjType.Methods.Concat(dumpedObjType.Constructors)
                .Where(m => m.Name == request.MethodName)
                .Where(m => m.Parameters.Count == paramsList.Count + 1) // TODO: Check types
                .ToList();

            if (overloads.Count == 0)
            {
                Debugger.Launch();
                Logger.Debug($"[MsvcDiver] Failed to Resolved method :/");
                return QuickError("Couldn't find method in type.");
            }
            if (overloads.Count > 1)
            {
                Debugger.Launch();
                Logger.Debug($"[MsvcDiver] Failed to Resolved method :/");
                return QuickError($"Too many matches for {request.MethodName} in type {request.TypeFullName}. Got: {overloads.Count}");
            }
            TypeDump.TypeMethod method = overloads.Single();

            Logger.Debug($"[MsvcDiver] Getting RTTI info objects from TypeFullName: {request.TypeFullName}");
            ParseFullTypeName(request.TypeFullName, out string rawAssemblyFilter, out string rawTypeFilter);
            var modulesAndTypes = _tricksterWrapper.SearchTypes(rawAssemblyFilter, rawTypeFilter);
            ModuleInfo module = modulesAndTypes.Keys.Single();
            Rtti.TypeInfo typeInfo = modulesAndTypes[module].Single();

            List<UndecoratedFunction> typeFuncs = _exportsMaster.GetExportedTypeFunctions(module, typeInfo.Name).ToList();
            UndecoratedFunction targetMethod = typeFuncs.Single(m => m.DecoratedName == method.DecoratedName);
            Logger.Debug($"[MsvcDiver] FOUND the target function: {targetMethod}");

            //
            // Turn target method into an invoke-able delegate
            //

            Type retType = method.ReturnTypeName.Equals("void", StringComparison.OrdinalIgnoreCase)
                ? typeof(void)
                : typeof(nuint);
            var delegateType = NativeDelegatesFactory.GetDelegateType(retType, method.Parameters.Count);
            var methodPtr = Marshal.GetDelegateForFunctionPointer(new IntPtr(targetMethod.Address), delegateType);

            //
            // Prepare parameters
            //
            Logger.Debug($"[MsvcDiver] Invoking {targetMethod} with 1 arg ('this'): 0x{objAddress:x16}");
            object[] invocationArgs = new object[method.Parameters.Count];
            invocationArgs[0] = objAddress;
            for (int i = 0; i < paramsList.Count; i++)
            {
                Logger.Debug($"[MsvcDiver] Invoking {targetMethod}, Decoding parameter #{i} (skipping 'this').");
                var decodedParam = paramsList[i];
                Logger.Debug($"[MsvcDiver] Invoking {targetMethod}, Decoded parameter #{i}, Is Null: {decodedParam == null}");
                nuint nuintParam = 0;
                if (decodedParam != null)
                {
                    Logger.Debug($"[MsvcDiver] Invoking {targetMethod}, Decoded parameter #{i}, Result Managed Type: {decodedParam?.GetType().Name}");
                    Logger.Debug($"[MsvcDiver] Invoking {targetMethod}, Casting parameter #{i} to nuint");
                    nuintParam = (nuint)(Convert.ToUInt64(decodedParam));
                }

                invocationArgs[i + 1] = nuintParam;
                Logger.Debug($"[MsvcDiver] Invoking {targetMethod}, Done with parameter #{i}");
            }

            //
            // Invoke target
            //
            nuint? results;
            try
            {
                results = methodPtr.DynamicInvoke(invocationArgs) as nuint?;
            }
            catch (Exception ex)
            {
                Logger.Debug("[MakeInvokeResponse] Threw an exception and we CAUGHT it. Ex: " + ex);
                throw new AggregateException(ex);
            }

            Logger.Debug("[MakeInvokeResponse] SUCCESS. Target didn't throw an exception.");

            //
            // Prepare invocation results for response
            //
            TypeDump returnTypeDump = null;
            if (targetMethod.RetType.Contains("::") && /*Is a pointer */ targetMethod.RetType.EndsWith("*"))
            {
                string normalizedRetType = method.ReturnTypeName[..^1]; // Remove '*' suffix
                returnTypeDump = GetRttiType(normalizedRetType);
            }

            InvocationResults invocResults;
            // Need to return the results. If it's primitive we'll encode it
            // If it's non-primitive we pin it and send the address.
            ObjectOrRemoteAddress returnValue;
            if (returnTypeDump == null || results is null or 0)
            {
                if (returnTypeDump != null)
                {
                    // This is a null pointer
                    returnValue = ObjectOrRemoteAddress.Null;
                }
                else
                {
                    // This is (probably) not a pointer. Hopefully just a primitive.
                    returnValue = ObjectOrRemoteAddress.FromObj(results);
                }
            }
            else
            {
                Logger.Debug($"[MsvcDiver] Invoking {targetMethod} result with a not-null OBJECT address");

                // Pinning results TODO
                string normalizedRetType = targetMethod.RetType;
                // Remove ' *' suffix, if exists
                normalizedRetType = normalizedRetType.EndsWith('*') ? normalizedRetType[..^1] : normalizedRetType;
                normalizedRetType = normalizedRetType.TrimEnd(' ');

                Logger.Debug(
                    $"[MsvcDiver] Trying to result the type of the returned object. Normalized return type from signature: {normalizedRetType}");
                ParseFullTypeName(normalizedRetType, out string retTypeRawAssemblyFilter, out string retTypeRawTypeFilter);


                Logger.Debug($"Dumping vftable (8 bytes) at the results address: 0x{results.Value:x16}");
                // TODO: Wrong for x86
                long vftable = Marshal.ReadInt64(new IntPtr((long)results.Value));
                Logger.Debug($"vftable: {vftable:x16}");
                Logger.Debug("Trying to resolve vftable to type...");
                Rtti.TypeInfo retTypeInfo = ResolveTypeFromVftableAddress((nuint)vftable);
                Logger.Debug($"Trying to resolve vftable to type... Got back: {retTypeInfo}");

                if (retTypeInfo != null)
                {
                    returnValue = ObjectOrRemoteAddress.FromToken(results.Value, retTypeInfo.FullTypeName);
                }
                else
                {
                    Logger.Debug("FAILED to resolve vftable to type. returning as nuint.");
                    returnValue = ObjectOrRemoteAddress.FromObj(results);
                }
            }

            invocResults = new InvocationResults()
            {
                VoidReturnType = false,
                ReturnedObjectOrAddress = returnValue
            };

            return JsonConvert.SerializeObject(invocResults);
        }

        private object ParseParameterObject(ObjectOrRemoteAddress param)
        {
            switch (param)
            {
                case { IsNull: true }:
                    return null;
                case { IsType: true }:
                    throw new NotImplementedException(
                        "A ObjectOrRemoteAddress with IsType=True was sent to a MSVC Diver.");
                case { IsRemoteAddress: false }:
                    return PrimitivesEncoder.Decode(param.EncodedObject, param.Type);
                case { IsRemoteAddress: true }:
                    // Look in freezer?
                    //if (_freezer.TryGetPinnedObject(param.RemoteAddress, out object pinnedObj))
                    {
                        return param.RemoteAddress;
                    }
            }

            Debugger.Launch();
            throw new NotImplementedException(
                $"Don't know how to parse this parameter into an object of type `{param.Type}`");
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
}
