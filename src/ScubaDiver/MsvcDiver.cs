using ScubaDiver.API;
using ScubaDiver.API.Hooking;
using ScubaDiver.API.Interactions;
using ScubaDiver.API.Interactions.Callbacks;
using ScubaDiver.API.Interactions.Dumps;
using ScubaDiver.API.Utils;
using ScubaDiver.Hooking;
using ScubaDiver.Rtti;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Runtime.InteropServices;
using System.Threading;

namespace ScubaDiver
{
    public class MsvcDiver : DiverBase
    {
        private MsvcTypesManager _typesManager = null;
        private MsvcOffensiveGC _offensiveGC = null;
        private MsvcFrozenItemsCollection _freezer = null;

        public MsvcDiver(IRequestsListener listener) : base(listener)
        {
            _responseBodyCreators["/gc"] = MakeGcHookModuleResponse;
            _responseBodyCreators["/gc_stats"] = MakeGcStatsResponse;
            _typesManager = new MsvcTypesManager();
        }

        public override void Start()
        {
            Logger.Debug("[MsvcDiver] Is logging debugs in release? " + Logger.DebugInRelease.Value);

            // Load or Hijack Newtonsoft.Json
            var nsJson = InitNewtonsoftJson();
            Logger.Debug("[MsvcDiver] Newtonsoft.Json's module path: " + nsJson.Location);

            base.Start();
        }



        protected string MakeGcHookModuleResponse(ScubaDiverMessage req)
        {
            string assemblyFilter = req.QueryString.Get("assembly");
            if (assemblyFilter == null || assemblyFilter == "*")
                return QuickError("'assembly' parameter can't be null or wildcard.");

            Predicate<string> moduleNameFilter = Filter.CreatePredicate(assemblyFilter);
            if (_offensiveGC == null)
            {
                _offensiveGC = new MsvcOffensiveGC();
                _freezer = new MsvcFrozenItemsCollection(_offensiveGC);
            }

            List<UndecoratedModule> undecoratedModules = _typesManager.GetUndecoratedModules(moduleNameFilter);
            try
            {
                _offensiveGC.HookModules(undecoratedModules);
                foreach (UndecoratedModule module in undecoratedModules)
                {
                    _offensiveGC.HookAllFreeFuncs(module, _typesManager.GetUndecoratedModules());
                }
            }
            catch (Exception e)
            {
                Logger.Debug($"[{nameof(MsvcDiver)}] {nameof(MakeGcHookModuleResponse)} Exception: " + e);
            }

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
        private IEqualityComparer<string> TypesComparer = new ParameterNamesComparer();

        protected override string MakeInjectDllResponse(ScubaDiverMessage req)
        {
            string dllPath = req.QueryString.Get("dll_path");
            if (string.IsNullOrEmpty(dllPath))
            {
                return QuickError("Missing 'dll_path' parameter");
            }

            try
            {
                string dllDirectory = Path.GetDirectoryName(dllPath);
                if (!Windows.Win32.PInvoke.SetDllDirectory(dllDirectory))
                {
                    Logger.Debug($"SetDllDirectory failed for: {dllDirectory} with error code: {Marshal.GetLastWin32Error()}");
                }
                
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
            var modules = _typesManager.GetUndecoratedModules();
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
            _typesManager.RefreshIfNeeded();
        }

        protected override Action HookFunction(FunctionHookRequest request, HarmonyWrapper.HookCallback patchCallback)
        {
            var hookPosition = (HarmonyPatchPosition)Enum.Parse(typeof(HarmonyPatchPosition), request.HookPosition);
            if (!Enum.IsDefined(typeof(HarmonyPatchPosition), hookPosition))
                throw new Exception("hook_position has an invalid or unsupported value");

            string rawTypeFilter = request.TypeFullName;
            string methodName = request.MethodName;
            ParseFullTypeName(rawTypeFilter, out var rawModuleFilter, out rawTypeFilter);

            MsvcType targetType = _typesManager.GetType(rawModuleFilter, rawTypeFilter).Upgrade();
            if (targetType == null)
                throw new Exception($"Failed to find type {rawTypeFilter} in module {rawModuleFilter}");

            // Find all methods with the requested name
            IEnumerable<MsvcMethod> overloads = targetType.GetMethods().Where(method => method.Name == methodName);
            IEnumerable<UndecoratedFunction> overloadsUndecFuncs = overloads.Select(func => func.UndecoratedFunc);
            // Find the specific overload with the right argument types
            UndecoratedFunction methodToHook = overloadsUndecFuncs.SingleOrDefault(method =>
                method.ArgTypes.Skip(1).SequenceEqual(request.ParametersTypeFullNames, TypesComparer));

            if (methodToHook == null)
                throw new Exception($"No matches for {methodName} in type {targetType}");

            Logger.Debug("[MsvcDiver] Hook Method - Resolved Method");
            Logger.Debug($"[MsvcDiver] Hooking function {methodName}...");

            // TODO: Is "nuint" return type always right here?
            DetoursNetWrapper.Instance.AddHook(targetType.TypeInfo, methodToHook, patchCallback, hookPosition);
            Logger.Debug($"[MsvcDiver] Hooked function {methodName}!");

            Action unhook = () =>
            {
                DetoursNetWrapper.Instance.RemoveHook(methodToHook, patchCallback);
            };
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
            string importerModule = req.QueryString.Get("importer_module");
            if (string.IsNullOrWhiteSpace(importerModule))
                importerModule = null;

            string typeFilter = req.QueryString.Get("type_filter");
            Logger.Debug("[MsvcDiver][Make<<<Types>>>>Response] filter: " + typeFilter);
            if (string.IsNullOrWhiteSpace(typeFilter))
                return QuickError("Missing parameter 'type_filter'");
            ParseFullTypeName(typeFilter, out var assemblyFilter, out typeFilter);

            Predicate<string> typeFilterPredicate = Filter.CreatePredicate(typeFilter);
            Predicate<string> moduleFilterPredicate = Filter.CreatePredicate(assemblyFilter);
            MsvcModuleFilter msvcModuleFilter = new MsvcModuleFilter()
            {
                NamePredicate = moduleFilterPredicate,
                ImportingModule = importerModule
            };

            IEnumerable<MsvcTypeStub> matchingTypes = _typesManager.GetTypes(msvcModuleFilter, typeFilterPredicate);

            List<TypesDump.TypeIdentifiers> types = new();
            foreach (MsvcTypeStub typeStub in matchingTypes)
            {
                string assembly = typeStub.TypeInfo.ModuleName;
                string fullTypeName = typeStub.TypeInfo.FullTypeName;
                // TODO: We might have multiple vftable addresses...
                ulong? xoredVftable = (typeStub.TypeInfo as FirstClassTypeInfo)?.XoredVftableAddress;
                types.Add(new TypesDump.TypeIdentifiers(assembly, fullTypeName, xoredVftable));
            }

            TypesDump dump = new()
            {
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
            Logger.Debug($"[MsvcDiver][MakeTypeResponse] Resolving type Name: {request.Assembly} {request.TypeFullName} vftable: 0x{request.MethodTableAddress:x16}");

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
            MsvcType matchingType = _typesManager.GetType(methodTableAddress)?.Upgrade();
            if (matchingType == null)
                return null;
            return TypeDumpFactory.ConvertMsvcTypeToTypeDump(matchingType);
        }

        private TypeDump GetTypeDump(string rawAssemblyFilter, string rawTypeFilter)
        {
            Predicate<string> moduleNameFilter = Filter.CreatePredicate(rawAssemblyFilter);
            Predicate<string> typeFilter = Filter.CreatePredicate(rawTypeFilter);
            MsvcType matchingType = _typesManager.GetType(moduleNameFilter, typeFilter)?.Upgrade();
            if (matchingType == null)
                return null;
            return TypeDumpFactory.ConvertMsvcTypeToTypeDump(matchingType);
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

        protected override string MakeHeapResponse(ScubaDiverMessage arg)
        {
            // Since trickster works on copied memory, we must refresh it so it copies again
            // the updated heap's state between invocations.
            RefreshRuntimeInternal(true);

            string rawFilter = arg.QueryString.Get("type_filter");
            ParseFullTypeName(rawFilter, out var rawAssemblyFilter, out var rawTypeFilter);

            Predicate<string> typeFilter = Filter.CreatePredicate(rawTypeFilter);
            Predicate<string> moduleNameFilter = Filter.CreatePredicate(rawAssemblyFilter);
            IEnumerable<MsvcTypeStub> matchingType = _typesManager.GetTypes(moduleNameFilter, typeFilter);

            //
            // Heap Search using Trickster
            //
            HeapDump output = new HeapDump();
            Logger.Debug($"[{DateTime.Now}] Starting Trickster Scan for class instances.");
            Dictionary<FirstClassTypeInfo, IReadOnlyCollection<ulong>> hits = _typesManager.Scan(matchingType);
            Logger.Debug($"[{DateTime.Now}] Trickster Scan finished with {hits.SelectMany(kvp => kvp.Value).Count()} results");
            foreach (var typeInstancesKvp in hits)
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
                foreach (var kvp in _offensiveGC.ClassInstances.Where(kvp => moduleNameFilter(kvp.Key)))
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
            string fullTypeName = arg.QueryString.Get("type_name");
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
                    ObjectDump alreadyFrozenObjDump = new ObjectDump()
                    {
                        Type = fullTypeName,
                        RetrivalAddress = objAddr,
                        PinnedAddress = objAddr,
                        HashCode = 0x0bad0bad
                    };
                    return JsonConvert.SerializeObject(alreadyFrozenObjDump);
                }

                // Search by vftable
                // TODO: Wrong for x86
                long vftable = Marshal.ReadInt64(new IntPtr((long)objAddr));
                MsvcType matchingType = _typesManager.GetType((nuint)vftable)?.Upgrade();
                if (matchingType == null)
                {
                    // Search by name instead
                    ParseFullTypeName(fullTypeName, out string assemblyFilter, out string typeFilter);
                    Predicate<string> typeFilterPredicate = Filter.CreatePredicate(typeFilter);
                    Predicate<string> moduleFilterPredicate = Filter.CreatePredicate(assemblyFilter);
                    matchingType = _typesManager.GetType(moduleFilterPredicate, typeFilterPredicate)?.Upgrade();
                    if (matchingType == null)
                    {
                        throw new Exception("Failed to resolve RTTI type by neither name not vftable value.");
                    }
                }
                Rtti.TypeInfo typeInfo = matchingType.TypeInfo;

                ulong pinningAddress = objAddr;
                if (pinningRequested)
                {
                    pinningAddress = _freezer.Pin(objAddr);
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
            Console.WriteLine($"[{nameof(MsvcDiver)}] MakeInvokeResponse Entered (!)");
            if (string.IsNullOrEmpty(arg.Body))
                return QuickError("Missing body");

            var request = JsonConvert.DeserializeObject<InvocationRequest>(arg.Body);
            if (request == null)
                return QuickError("Failed to deserialize body");
            nuint objAddress = (nuint)request.ObjAddress;
            if (objAddress == 0)
                return QuickError("Calling a instance-less function is not implemented for MSVC");

            // Need to figure target instance and the target type.
            ParseFullTypeName(request.TypeFullName, out string rawAssemblyFilter, out string rawTypeFilter);
            MsvcTypeStub msvcTypeStub = _typesManager.GetType(rawAssemblyFilter, rawTypeFilter);
            MsvcType msvcType = msvcTypeStub.Upgrade();
            // Check if we have this objects in our pinned pool
            // TODO: Pull from freezer?

            //
            // We have our target and it's Type Dump. Now look for a matching overload for the
            // function to invoke.
            //
            List<object> paramsList = new();
            if (request.Parameters.Any())
            {
                paramsList = request.Parameters.Select(ParseParameterObject).ToList();
            }

            // Search the method/ctor with the matching signature
            List<MsvcMethod> overloads =
                msvcType.GetMethods()
                .Where(m => m.Name == request.MethodName || m.UndecoratedFunc.DecoratedName == request.MethodName)
                .Where(m => m.UndecoratedFunc.NumArgs == paramsList.Count + 1) // TODO: Check types
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
            UndecoratedFunction method = overloads.Single().UndecoratedFunc;


            List<UndecoratedFunction> typeFuncs = msvcType.GetMethods().Select(msvcMethod => msvcMethod.UndecoratedFunc).ToList();
            UndecoratedFunction targetMethod = typeFuncs.SingleOrDefault(m => m.DecoratedName == method.DecoratedName);
            if (targetMethod == null)
            {
                // Extend search to other types. Useful when:
                //  * The method is inherited and hence found under another type's name.
                //  * The object was casted from one module to another: module_1!class_name -> module_2!class_name
                //
                // Extracting the "owning" type of the method, which we now assume is different then the object's type.
                // Turning `namespace::method_owner_type::func` to `namespace::method_owner_type`
                string methodFullName = method.UndecoratedFullName;
                string methodOwnerTypeFullName = methodFullName.Substring(0, methodFullName.LastIndexOf(method.UndecoratedName)).TrimEnd(':');

                ParseFullTypeName(methodOwnerTypeFullName, out string methodOwnerModuleName, out string methodOwnerTypeName);
                MsvcType methodOwnerType = _typesManager.GetType(methodOwnerModuleName, methodOwnerTypeName).Upgrade();

                typeFuncs = methodOwnerType.GetMethods().Select(msvcMethod => msvcMethod.UndecoratedFunc).ToList();
                targetMethod = typeFuncs.SingleOrDefault(m => m.DecoratedName == method.DecoratedName);
                if (targetMethod != null)
                {
                    // Found the target function in a PARENT/DIFFERENT type!
                }
                else
                {
                    return QuickError($"Could not find method {targetMethod} in either {msvcType.Name} nor {methodOwnerType}");
                }
            }

            //
            // Turn target method into an invoke-able delegate
            //

            // TODO: What about doubles/floats return vaslues?
            Type retType = method.RetType.Equals("void", StringComparison.OrdinalIgnoreCase)
                ? typeof(void)
                : typeof(nuint);
            int floatsBitmap = NativeDelegatesFactory.GetFloatsBitmap(method.ArgTypes, p => p == "float" || p == "double");
            var delegateType = NativeDelegatesFactory.GetDelegateType(retType, method.NumArgs.Value, floatsBitmap);
            var methodPtr = Marshal.GetDelegateForFunctionPointer(new IntPtr((long)targetMethod.Address), delegateType);

            //
            // Prepare parameters
            //
            object[] invocationArgs = new object[method.NumArgs.Value];
            invocationArgs[0] = objAddress;
            for (int i = 0; i < paramsList.Count; i++)
            {
                var decodedParam = paramsList[i];
                if (decodedParam is float || decodedParam is double)
                {
                    double doubleParam = (double)Convert.ToDouble(decodedParam);
                    invocationArgs[i + 1] = doubleParam;
                }
                else
                {
                    nuint nuintParam = 0;
                    if (decodedParam != null)
                    {
                        nuintParam = (nuint)(Convert.ToUInt64(decodedParam));
                    }

                    invocationArgs[i + 1] = nuintParam;
                }
            }

            //
            // Invoke target
            //
            bool resIsDouble = false;
            nuint? resultsNuint = null;
            double? resultsDouble = null;
            try
            {
                object resultsObj = methodPtr.DynamicInvoke(invocationArgs);
                if (resultsObj is double)
                {
                    resultsDouble = (double)resultsObj;
                    resIsDouble = true;
                }
                else
                {
                    resultsNuint = resultsObj as nuint?;
                }
            }
            catch (Exception ex)
            {
                Logger.Debug("[MakeInvokeResponse] Threw an exception and we CAUGHT it. Ex: " + ex);
                throw new AggregateException(ex);
            }

            //
            // Prepare invocation results for response
            //
            MsvcTypeStub returnTypeDump = null;
            if (!resIsDouble)
            {
                // TODO: The '::' check is a hack to determine if it's a pointer.
                // Types don't have to be defined within a namespace, so this is not a good check.
                if (targetMethod.RetType.Contains("::") && /*Is a pointer */ targetMethod.RetType.EndsWith("*"))
                {
                    string normalizedRetType = method.RetType[..^1]; // Remove '*' suffix
                    ParseFullTypeName(normalizedRetType, out var retTypeAssemblyFilter, out var retTypeFilter);
                    Predicate<string> moduleNameFilter = Filter.CreatePredicate(retTypeAssemblyFilter);
                    Predicate<string> typeNameFilter = Filter.CreatePredicate(retTypeFilter);
                    returnTypeDump = _typesManager.GetType(moduleNameFilter, typeNameFilter);
                    if (returnTypeDump == null)
                    {
                        // Retry with "importing module filter" (Will only help if we found TOO MANY results, and not zero)
                        MsvcModuleFilter moduleFilter = new MsvcModuleFilter()
                        {
                            NamePredicate = moduleNameFilter,
                            ImportingModule = msvcType.Module.Name
                        };
                        returnTypeDump = _typesManager.GetType(moduleFilter, typeNameFilter);
                        if (returnTypeDump == null)
                        {
                            // Maybe it's just our current type
                            if (typeNameFilter(msvcTypeStub.TypeInfo.NamespaceAndName))
                            {
                                returnTypeDump = msvcTypeStub;
                            }
                        }
                    }
                }
            }

            InvocationResults invocResults;
            // Need to return the results. If it's primitive we'll encode it
            // If it's non-primitive we pin it and send the address.
            ObjectOrRemoteAddress returnValue;
            if (returnTypeDump == null || resultsNuint is null or 0)
            {
                if (returnTypeDump != null || resultsNuint == null)
                {
                    // This is a null pointer
                    returnValue = ObjectOrRemoteAddress.Null;
                }
                else
                {
                    // This is (probably) not a pointer. Hopefully just a primitive.
                    if (resIsDouble)
                        returnValue = ObjectOrRemoteAddress.FromObj(resultsDouble);
                    else
                        returnValue = ObjectOrRemoteAddress.FromObj(resultsNuint);
                }
            }
            else
            {
                MsvcType matchingType = returnTypeDump?.Upgrade();
                if (matchingType == null) // TODO: This is dead code, I think
                {
                    // This vftable resolution USED to work, but I think it broke in the great "types" refactor.
                    // TODO: Wrong for x86
                    long vftable = Marshal.ReadInt64(new IntPtr((long)resultsNuint.Value));
                    _typesManager.GetType((nuint)vftable)?.Upgrade();
                }
                Rtti.TypeInfo retTypeInfo = matchingType?.TypeInfo;

                if (retTypeInfo != null)
                {
                    returnValue = ObjectOrRemoteAddress.FromToken(resultsNuint.Value, retTypeInfo.FullTypeName);
                }
                else
                {
                    returnValue = ObjectOrRemoteAddress.FromObj(resultsNuint);
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
