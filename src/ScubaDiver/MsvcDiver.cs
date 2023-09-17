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
using ScubaDiver.API.Interactions;
using ScubaDiver.API;
using System.Runtime.InteropServices;
using System.Threading;
using ScubaDiver.API.Interactions.Callbacks;
using ScubaDiver.Hooking;
using Windows.Win32.Foundation;

namespace ScubaDiver
{
    public class MsvcDiver : DiverBase
    {
        private TricksterWrapper _tricksterWrapper = null;
        private ExportsMaster _exportsMaster = null;

        public MsvcDiver(IRequestsListener listener) : base(listener)
        {
            _responseBodyCreators["/gc"] = MakeGcResponse;

            _exportsMaster = new ExportsMaster();
            _tricksterWrapper = new TricksterWrapper(_exportsMaster);
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
            throw new NotImplementedException(
                "Offensive GC is turned off until 'new' operator searching is re-enabled.");

            //Logger.Debug($"[{nameof(MsvcDiver)}] {nameof(MakeGcResponse)} IN!");
            //List<UndecoratedModule> undecModules = _tricksterWrapper.GetUndecoratedModules();
            //Logger.Debug($"[{nameof(MsvcDiver)}] {nameof(MakeGcResponse)} Init'ing GC");
            //try
            //{
            //    gc = new MsvcOffensiveGC();
            //    gc.Init(undecModules);
            //}
            //catch (Exception e)
            //{
            //    Logger.Debug($"[{nameof(MsvcDiver)}] {nameof(MakeGcResponse)} Exception: " + e);
            //}

            //Logger.Debug($"[{nameof(MsvcDiver)}] {nameof(MakeGcResponse)} OUT!");
            //return "{\"status\":\"ok\"}";
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



        protected override void RefreshRuntime()
        {
            Logger.Debug($"[{DateTime.Now}][MsvcDiver][RefreshRuntime] Refreshing runtime!");
            if (!_tricksterWrapper.RefreshRequired())
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
            TypeInfo[] typeInfos = modulesToTypes[module].ToArray();
            if (typeInfos.Length != 1)
                QuickError($"Expected exactly 1 match for type, got {typeInfos.Length}");


            TypeInfo typeInfo = typeInfos.Single();
            List<UndecoratedSymbol> members = _exportsMaster.GetExportedTypeMembers(module, typeInfo.Name).ToList();
            UndecoratedSymbol vftable = members.FirstOrDefault(member => member.UndecoratedName.EndsWith("`vftable'"));


            List<UndecoratedFunction> exportedFuncs = members.OfType<UndecoratedFunction>().ToList();
            List<UndecoratedFunction> virtualFuncs = new List<UndecoratedFunction>();
            if (vftable != null)
            {

                virtualFuncs = VftableParser.AnalyzeVftable(_tricksterWrapper.GetProcessHandle(), module, _exportsMaster.GetExports(module), vftable);

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
            DetoursNetWrapper.HookCallback hook =
                (DetoursMethodGenerator.DetoursTrampoline tramp, object[] args, out nuint value) =>
                {
                    if (args.Length == 0)
                        throw new Exception(
                            "Bad arguments to unmanaged HookCallback. Expecting at least 1 (for 'this').");

                    object self = new NativeObject((nuint)args.FirstOrDefault(), typeInfo);

                    // Args without self
                    object[] argsToForward = new object[args.Length - 1];
                    for (int i = 0; i < argsToForward.Length; i++)
                    {
                        nuint arg = (nuint)args[i + 1];

                        // If the argument is a pointer, indicate it with a NativeObject
                        string argType = tramp.Target.ArgTypes[i + 1];
                        if (argType == "char*" || argType == "char *")
                        {
                            if (arg != 0)
                            {
                                string cString = Marshal.PtrToStringAnsi(new IntPtr((long)arg));
                                argsToForward[i] = new CharStar(arg, cString);
                            }
                            else
                            {
                                argsToForward[i] = arg;
                            }
                        }
                        else if (argType.EndsWith('*'))
                        {
                            // TODO: SecondClassTypeInfo is abused here
                            string fixedArgType = argType[..^1].Trim();
                            argsToForward[i] = new NativeObject(arg,
                                new SecondClassTypeInfo(module.Name, fixedArgType));
                        }
                        else
                        {
                            // Primitive or struct or something else crazy
                            argsToForward[i] = arg;
                        }
                    }


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

            // Call callback at controller
            InvocationResults hookCallbackResults = reverseCommunicator.InvokeCallback(token, stackTrace, remoteParams);

            return hookCallbackResults.ReturnedObjectOrAddress;
        }

        protected override string MakeTypesResponse(ScubaDiverMessage req)
        {
            string assemblyFilter = req.QueryString.Get("assembly");
            Predicate<string> assmFilter = Filter.CreatePredicate(assemblyFilter);

            List<UndecoratedModule> matchingAssemblies = _tricksterWrapper.GetUndecoratedModules(assmFilter).ToList();
            List<TypesDump.TypeIdentifiers> types = new();
            if (matchingAssemblies.Count == 0)
            {
                return QuickError($"No modules matched the filter '{assemblyFilter}'");
            }
            if (matchingAssemblies.Count > 1)
            {
                string assembliesList = string.Join(", ", matchingAssemblies.Select(x=>x.Name).ToArray());
                return QuickError(
                    $"Too many modules matched the filter '{assemblyFilter}'. Found {matchingAssemblies.Count} ({assembliesList})");
            }

            UndecoratedModule assm = matchingAssemblies.Single();
            foreach (TypeInfo type in assm.Types)
            {
                types.Add(new TypesDump.TypeIdentifiers()
                {
                    TypeName = $"{assm.Name}!{type.Name}"
                });
            }

            TypesDump dump = new()
            {
                AssemblyName = assm.Name,
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

            TypeDump dump = GetRttiType(request.TypeFullName, request.Assembly);
            if (dump != null)
                return JsonConvert.SerializeObject(dump);

            return QuickError("Failed to find type in searched assemblies");
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
            Logger.Debug($"[GetTypeDump] Querying for rawAssemblyFilter: {rawAssemblyFilter}, rawTypeFilter: {rawTypeFilter}");
            var modulesAndTypes = _tricksterWrapper.SearchTypes(rawAssemblyFilter, rawTypeFilter);

            foreach (KeyValuePair<ModuleInfo, IEnumerable<TypeInfo>> moduleAndTypes in modulesAndTypes)
            {
                ModuleInfo module = moduleAndTypes.Key;
                IReadOnlyList<UndecoratedSymbol> exports = _exportsMaster.GetExports(module);
                foreach (TypeInfo typeInfo in moduleAndTypes.Value)
                {
                    UndecoratedSymbol vftable;
                    List<TypeDump.TypeField> fields = new();
                    List<TypeDump.TypeMethod> methods = new();
                    List<TypeDump.TypeMethod> constructors = new();
                    DeconstructRttiType(typeInfo, module, fields, constructors, methods, out vftable);

                    if (vftable != null)
                    {
                        HANDLE process = _tricksterWrapper.GetProcessHandle();
                        List<UndecoratedFunction> virtualFunctionsInternal = VftableParser.AnalyzeVftable(process, module, exports, vftable);
                        List<TypeDump.TypeMethod> virtualFunctions = virtualFunctionsInternal.Select(VftableParser.ConvertToTypeMethod).Where(x => x != null).ToList();

                        foreach (TypeDump.TypeMethod virtualFunction in virtualFunctions)
                        {
                            bool exists = methods.Any(existingMethod =>
                                existingMethod.UndecoratedFullName == virtualFunction.UndecoratedFullName);
                            if (exists)
                                continue;

                            methods.Add(virtualFunction);
                        }
                    }

                    TypeDump recusiveTypeDump = new TypeDump()
                    {
                        Assembly = module.Name,
                        Type = typeInfo.Name,
                        Methods = methods,
                        Constructors = constructors,
                        Fields = fields
                    };
                    return recusiveTypeDump;
                }
            }

            return null;
        }

        private void DeconstructRttiType(TypeInfo typeInfo,
            ModuleInfo module,
            List<TypeDump.TypeField> fields,
            List<TypeDump.TypeMethod> constructors,
            List<TypeDump.TypeMethod> methods,
            out UndecoratedSymbol vftable)
        {
            vftable = null;

            string className = typeInfo.Name.Substring(typeInfo.Name.LastIndexOf("::") + 2);
            string ctorName = $"{typeInfo.Name}::{className}"; // Constructing NameSpace::ClassName::ClassName
            string vftableName = $"{typeInfo.Name}::`vftable'"; // Constructing NameSpace::ClassName::`vftable
            foreach (UndecoratedSymbol dllExport in _exportsMaster.GetExportedTypeMembers(module, typeInfo.Name))
            {
                if (dllExport is UndecoratedFunction undecFunc)
                {
                    var typeMethod = VftableParser.ConvertToTypeMethod(undecFunc);
                    if (typeMethod == null)
                    {
                        Logger.Debug($"[MsvcDiver] Failed to convert UndecoratedFunction: {undecFunc.UndecoratedFullName}. Skipping.");
                        continue;
                    }

                    if (typeMethod.UndecoratedFullName == ctorName)
                        constructors.Add(typeMethod);
                    else
                        methods.Add(typeMethod);
                }
                else if (dllExport is UndecoratedExportedField undecField)
                {
                    HandleTypeField(undecField, vftableName, fields, ref vftable);
                }
            }
        }

        private void HandleTypeField(UndecoratedExportedField undecField, string vftableName,
            List<TypeDump.TypeField> fields, ref UndecoratedSymbol vftable)
        {
            // TODO: Fields could be exported as well..
            // we only expected the "vftable" field (not actually a field...) and methods/ctors right now

            if (undecField.UndecoratedFullName == vftableName)
            {
                if (vftable != null)
                {
                    Logger.Debug(
                        $"Duplicate vftable export found. Old: {vftable.UndecoratedFullName} , New: {undecField.UndecoratedFullName}");
                    return;
                }

                vftable = undecField;


                fields.Add(new TypeDump.TypeField()
                {
                    Name = "vftable",
                    TypeFullName = undecField.UndecoratedFullName,
                    Visibility = "Public"
                });

                // Keep vftable aside so we can also gather functions from it
                vftable = undecField;
                return;
            }

            Logger.Debug(
                $"[{nameof(GetTypeDump)}] Unexpected exported field. Undecorated name: {undecField.UndecoratedFullName}");
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
            RefreshRuntime();

            string rawFilter = arg.QueryString.Get("type_filter");
            ParseFullTypeName(rawFilter, out var rawAssemblyFilter, out var rawTypeFilter);

            Predicate<string> assmFilter = Filter.CreatePredicate(rawAssemblyFilter);
            Predicate<string> typeFilter = Filter.CreatePredicate(rawTypeFilter);

            HeapDump hd = new HeapDump()
            {
                Objects = new List<HeapDump.HeapObject>()
            };

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

            Logger.Debug($"[{DateTime.Now}] Starting Trickster Scan for class instances.");
            Dictionary<FirstClassTypeInfo, IReadOnlyCollection<ulong>> addresses = _tricksterWrapper.Scan(allClassesToScanFor);
            Logger.Debug($"[{DateTime.Now}] Trickster Scan finished with {addresses.SelectMany(kvp => kvp.Value).Count()} results");
            foreach (var typeInstancesKvp in addresses)
            {
                FirstClassTypeInfo typeInfo = typeInstancesKvp.Key;
                foreach (nuint addr in typeInstancesKvp.Value)
                {
                    HeapDump.HeapObject ho = new HeapDump.HeapObject()
                    {
                        Address = addr,
                        MethodTable = typeInfo.VftableAddress, // TODO: Send XOR'd value instead?
                        Type = typeInfo.FullTypeName
                    };
                    hd.Objects.Add(ho);
                }
            }

            string json = JsonConvert.SerializeObject(hd);

            // Trying to get rid of the VFTable addresses from our heap.
            hd.Objects.ForEach(heapObj => heapObj.MethodTable = 0xdeadc0de);

            return json;

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

            try
            {
                // TODO: Wrong for x86
                long vftable = Marshal.ReadInt64(new IntPtr((long)objAddr));
                TypeInfo typeInfo = ResolveTypeFromVftableAddress((nuint)vftable);
                if (typeInfo == null)
                {
                    throw new Exception("Failed to resolve vftable of target to any RTTI type.");
                }

                // TODO: Actual Pin
                ulong pinAddr = 0x0;

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
            object instance = null;
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
            TypeInfo typeInfo = modulesAndTypes[module].Single();

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
            nuint? results = methodPtr.DynamicInvoke(invocationArgs) as nuint?;

            //
            // Prepare invocation results for response
            //
            TypeDump returnTypeDump = null;
            if (targetMethod.RetType.Contains("::") && /*Is a pointer */ targetMethod.RetType.EndsWith(" *"))
            {
                string normalizedRetType = method.ReturnTypeName[..^2]; // Remove ' *' suffix
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
                TypeInfo retTypeInfo = ResolveTypeFromVftableAddress((nuint)vftable);
                Logger.Debug($"Trying to resolve vftable to type... Got back: {retTypeInfo}");

                if (retTypeInfo != null)
                {
                    ulong pinAddr = 0x0;

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
                    break;
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
