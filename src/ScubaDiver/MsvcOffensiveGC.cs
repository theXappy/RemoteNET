using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using ScubaDiver.Hooking;
using DetoursNet;
using ScubaDiver.API.Hooking;
using ScubaDiver.Rtti;
using System.Collections.Concurrent;
using NtApiDotNet.Win32;
using Windows.Win32;

namespace ScubaDiver
{
    public class NamedDict<T> : Dictionary<string, T>
    {
        public NamedDict<T> DeepCopy()
        {
            NamedDict<T> res = new NamedDict<T>();
            if (typeof(T) == typeof(NamedDict<>))
            {
                var innerDeepCopy = typeof(NamedDict<>).GetMethod(nameof(DeepCopy));
                foreach (KeyValuePair<string, T> keyValuePair in this)
                {
                    res[keyValuePair.Key] = (T)innerDeepCopy.Invoke(keyValuePair.Value, Array.Empty<object>());
                }
            }
            else
            {
                // Easy mode
                foreach (KeyValuePair<string, T> keyValuePair in this)
                {
                    res[keyValuePair.Key] = keyValuePair.Value;
                }
            }
            return res;
        }
    }

    internal static class FreeFinder
    {
        public static Dictionary<ModuleInfo, DllExport> Find(List<UndecoratedModule> modules, string name)
        {
            void ProcessSingleModule(UndecoratedModule module,
                Dictionary<ModuleInfo, DllExport> workingDict)
            {
                if (!module.TryGetRegularTypelessFunc(name, out List<NtApiDotNet.Win32.DllExport> methodGroup))
                    return;

                var firstPtr = methodGroup.Single();

                workingDict[module.ModuleInfo] = firstPtr;
                // TODO: Am I missing matches if there are multiple exports called 'free'?
                return;
            }

            Dictionary<ModuleInfo, DllExport> res = new();
            foreach (UndecoratedModule module in modules)
            {
                ProcessSingleModule(module, res);
            }
            return res;
        }
    }


    internal class MsvcOffensiveGC
    {
        private string operatorNewName = "operator new";
        private static HashSet<string> _alreadyHookedDecorated = new();

        // Object Tracking
        private static readonly object _classToInstancesLock = new();
        private static readonly NamedDict<NamedDict<HashSet<nuint>>> _moduleToClasses = new();
        public IReadOnlyDictionary<string, IReadOnlyDictionary<string, HashSet<nuint>>> ClassInstances
        {
            get
            {
                lock (_classSizesLock)
                {
                    return _moduleToClasses.ToDictionary(kvp => kvp.Key,
                            kvp =>
                                (IReadOnlyDictionary<string, HashSet<nuint>>)kvp.Value.ToDictionary(
                                    kvp2 => kvp2.Key,
                                    kvp2 => new HashSet<nuint>(kvp2.Value)));
                }
            }
        }

        // Size Match-Making
        private static readonly object _addressToSizeLock = new();
        private static readonly LimitedSizeDictionary<nuint, nuint> _addressToSize = new(1000);
        private static readonly object _classSizesLock = new();
        private static readonly NamedDict<nuint> _classSizes = new();
        public IReadOnlyDictionary<string, nuint> ClassSizes
        {
            get
            {
                lock (_classSizesLock)
                {
                    return _classSizes.DeepCopy();
                }
            }
        }
        public IReadOnlyDictionary<nuint, nuint> AddressesSizes
        {
            get
            {
                lock (_addressToSizeLock)
                {
                    return _addressToSize.ToDictionary(kvp => kvp.Key, kvp => kvp.Value);
                }
            }
        }

        private List<UndecoratedModule> _alreadyHookedModules = new List<UndecoratedModule>();
        public void HookModule(UndecoratedModule module) => HookModules(new List<UndecoratedModule>() { module });
        public void HookModules(List<UndecoratedModule> modules)
        {
            modules = modules.Where(m => !_alreadyHookedModules.Contains(m)).ToList();

            Dictionary<TypeInfo, UndecoratedFunction> initMethods = GetAutoClassInit2Funcs(modules);
            Dictionary<TypeInfo, List<UndecoratedFunction>> ctors = GetCtors(modules);
            Dictionary<TypeInfo, List<UndecoratedFunction>> dtors = GetDtors(modules);
            Dictionary<string, List<UndecoratedFunction>> newOperators = GetNewOperators(modules);

            HookAutoClassInit2Funcs(initMethods);
            HookCtors(ctors);
            HookDtors(dtors);
            HookNewOperators(newOperators);

            _alreadyHookedModules.AddRange(modules);
        }

        public void HookAllFreeFuncs(UndecoratedModule target, List<UndecoratedModule> allModules)
        {
            // Make sure our C++ Helper is loaded before accessing anything from the `MsvcOffensiveGcHelper` class
            // otherwise the loading the P/Invoke methods will fail on "Failed to load DLL.
            System.Reflection.Assembly assm = typeof(MsvcOffensiveGC).Assembly;
            string assmDir = System.IO.Path.GetDirectoryName(assm.Location);
            string helperPath = System.IO.Path.Combine(assmDir, "MsvcOffensiveGcHelper.dll");
            var res = PInvoke.LoadLibrary(helperPath);

            int attemptedFreeFuncs = 0;
            foreach (string funcName in new[] { "free", "_free", "_free_dbg" })
            {
                // Logger.Debug($"[{nameof(MsvcOffensiveGC)}] Starting to hook '{funcName}'s...");
                Dictionary<ModuleInfo, DllExport> funcs = FreeFinder.Find(allModules, funcName);
                if (funcs.Count == 0)
                {
                    // Logger.Debug($"[{nameof(MsvcOffensiveGC)}] WARNING! '{funcName}' was not found.");
                    continue;
                }
                if (funcs.Count > 1)
                {
                    // Logger.Debug($"[{nameof(MsvcOffensiveGC)}] WARNING! Found '{funcName}' in more then 1 module: " +string.Join(", ", funcs.Keys.Select(a => a.Name).ToArray()));
                }
                foreach (var kvp in funcs)
                {
                    DllExport freeFunc = kvp.Value;
                    // Logger.Debug($"[{nameof(MsvcOffensiveGC)}] Chose '{funcName}' from {kvp.Key.Name}");

                    // Find out native replacement function for the given func name
                    IntPtr replacementPtr = MsvcOffensiveGcHelper.GetOrAddReplacement((IntPtr)freeFunc.Address);

                    // Logger.Debug($"[{nameof(MsvcOffensiveGC)}] Hooking '{funcName}' at 0x{freeFunc.Address:X16} (from {kvp.Key.Name}), " +
                        //$"in the IAT of {target.Name}. " +
                        //$"Replacement Address: 0x{replacementPtr:X16}");
                    ModuleInfo targetModule = target.ModuleInfo;
                    bool replacementRes = Loader.HookIAT((IntPtr)(ulong)targetModule.BaseAddress, (IntPtr)freeFunc.Address, replacementPtr);


                    // Logger.Debug($"[{nameof(MsvcOffensiveGC)}] res = {replacementRes}");
                    if (replacementRes)
                    {
                        // Found the right import! Breaking so we don't override the "original free ptr" with wrong matches.
                        // Logger.Debug($"[{nameof(MsvcOffensiveGC)}] [@@@@@@@] Found the right import! Breaking.");
                        attemptedFreeFuncs++;
                        break;
                    }
                    else
                    {
                        // Logger.Debug($"[{nameof(MsvcOffensiveGC)}] [xxxxxxx] Wrong import.");
                    }
                }
            }

            // Logger.Debug($"[{nameof(MsvcOffensiveGC)}] Done hooking free funcs. attempted to hook: {attemptedFreeFuncs} funcs");
        }

        private Dictionary<string, List<UndecoratedFunction>> GetNewOperators(List<UndecoratedModule> modules)
        {
            void ProcessSingleModule(UndecoratedModule module,
                Dictionary<string, List<UndecoratedFunction>> workingDict)
            {
                List<UndecoratedFunction> tempList = new List<UndecoratedFunction>();
                //if (module.TryGetTypelessFunc(kMangledNew64, out var methodGroup))
                //{
                //    tempList.AddRange(methodGroup);
                //}

                //if (module.TryGetTypelessFunc(kMangledNewNothrow64, out methodGroup))
                //{
                //    tempList.AddRange(methodGroup);
                //}

                // Logger.Debug($"[{nameof(MsvcOffensiveGC)}] Looking for {operatorNewName} in {module.Name}");
                if (module.TryGetUndecoratedTypelessFunc(operatorNewName, out var methodGroup))
                {
                    // Logger.Debug($"[{nameof(MsvcOffensiveGC)}] FOUND {operatorNewName} in {module.Name}");
                    tempList.AddRange(methodGroup);
                }
                else
                {
                    // Logger.Debug($"[{nameof(MsvcOffensiveGC)}] Did NOT find {operatorNewName} in {module.Name}");
                }

                if (tempList.Any())
                {
                    workingDict[module.Name] = tempList;
                }
            }

            Dictionary<string, List<UndecoratedFunction>> res = new();
            foreach (UndecoratedModule module in modules)
            {
                ProcessSingleModule(module, res);
            }

            return res;
        }
        private Dictionary<TypeInfo, List<UndecoratedFunction>> GetCtors(List<UndecoratedModule> modules)
        {
            string GetCtorName(TypeInfo type)
            {
                string fullTypeName = type.Name;
                string className = fullTypeName;
                if (fullTypeName.Contains("::"))
                {
                    className = fullTypeName.Substring(fullTypeName.LastIndexOf("::") + 2);
                }
                return $"{fullTypeName}::{className}";
            }

            void ProcessSingleModule(UndecoratedModule module,
                Dictionary<TypeInfo, List<UndecoratedFunction>> workingDict)
            {
                foreach (TypeInfo type in module.Types)
                {
                    string ctorName = GetCtorName(type);
                    if (!module.TryGetTypeFunc(type, ctorName, out var ctors))
                        continue;

                    // Found the method group
                    workingDict[type] = ctors;
                }
            }

            Dictionary<TypeInfo, List<UndecoratedFunction>> res = new();
            foreach (UndecoratedModule module in modules)
            {
                ProcessSingleModule(module, res);
            }

            return res;
        }
        private Dictionary<TypeInfo, List<UndecoratedFunction>> GetDtors(List<UndecoratedModule> modules)
        {
            string GetDtorName(TypeInfo type)
            {
                string fullTypeName = type.Name;
                string className = fullTypeName;
                if (fullTypeName.Contains("::"))
                {
                    className = fullTypeName.Substring(fullTypeName.LastIndexOf("::") + 2);
                }
                return $"{fullTypeName}::~{className}";
            }

            void ProcessSingleModule(UndecoratedModule module,
                Dictionary<TypeInfo, List<UndecoratedFunction>> workingDict)
            {
                foreach (TypeInfo type in module.Types)
                {
                    string dtorName = GetDtorName(type);
                    if (!module.TryGetTypeFunc(type, dtorName, out var dtors))
                        continue;

                    // Found the method group
                    workingDict[type] = dtors;
                }
            }

            Dictionary<TypeInfo, List<UndecoratedFunction>> res = new();
            foreach (UndecoratedModule module in modules)
            {
                ProcessSingleModule(module, res);
            }

            return res;
        }
        private Dictionary<TypeInfo, UndecoratedFunction> GetAutoClassInit2Funcs(List<UndecoratedModule> modules)
        {
            void ProcessSingleModule(UndecoratedModule module,
                Dictionary<TypeInfo, UndecoratedFunction> workingDict)
            {
                foreach (TypeInfo type in module.Types)
                {
                    string methondName = $"{type.Name}::__autoclassinit2";
                    if (!module.TryGetTypeFunc(type, methondName, out var methodGroup))
                        continue;
                    // Found the method group (all overloads with the same name)
                    if (methodGroup.Count != 1)
                    {
                        // Logger.Debug($"Expected exactly one __autoclassinit2 function for type {type.Name}, Found {methodGroup.Count}");
                        continue;
                    }

                    var func = methodGroup.Single();
                    workingDict[type] = func;
                }
            }

            Dictionary<TypeInfo, UndecoratedFunction> res = new();
            foreach (UndecoratedModule module in modules)
            {
                ProcessSingleModule(module, res);
            }

            return res;
        }

        // NEW OPERATORS
        private static void HookNewOperators(Dictionary<string, List<UndecoratedFunction>> newOperators)
        {
            // Hook all new operators
            // Logger.Debug($"[{nameof(MsvcOffensiveGC)}] Starting to hook 'operator new's...");
            int attemptedOperatorNews = 0;
            foreach (var moduleToFuncs in newOperators)
            {
                foreach (var newOperator in moduleToFuncs.Value)
                {
                    attemptedOperatorNews++;
                    // Logger.Debug($"[{nameof(MsvcOffensiveGC)}] Hooking 'operator new' at 0x{newOperator.Address:x16} ({newOperator.Module})");

                    //DetoursNetWrapper.Instance.AddHook(newOperator, UnifiedOperatorNew);
                    DetoursNetWrapper.Instance.AddHook(TypeInfo.Dummy, newOperator, UnifiedOperatorNew, HarmonyPatchPosition.Postfix);
                }
            }

            // Logger.Debug($"[{nameof(MsvcOffensiveGC)}] Done hooking 'operator new' s. attempted to hook: {attemptedOperatorNews} funcs");
            // Logger.Debug($"[{nameof(MsvcOffensiveGC)}] Done hooking 'operator new' s. DelegateStore.Mine.Count: {DelegateStore.Mine.Count}");
        }
        private static bool UnifiedOperatorNew(object sizeObj, object[] args, ref object retValue)
        {
            if (sizeObj is NativeObject sizeNativeObj && retValue is nuint retNuint)
            {
                nuint size = sizeNativeObj.Address;
                nuint allocatedObjAddress = retNuint;
                RegisterSize(allocatedObjAddress, size);
            }
            else
            {
                // Logger.Debug($"[UnifiedOperatorNew] sizeObj: {sizeObj}");
            }

            return true; // Don't skip original
        }
        private delegate nuint OperatorNewType(nuint size);

        // CTORs
        private static void HookCtors(Dictionary<TypeInfo, List<UndecoratedFunction>> ctors)
        {
            // Hook all ctors
            // Logger.Debug($"[{nameof(MsvcOffensiveGC)}] Starting to hook ctors...");
            int attemptedHookedCtorsCount = 0;
            foreach (var kvp in ctors)
            {
                TypeInfo type = kvp.Key;
                string fullTypeName = $"{type.ModuleName}!{type.Name}";
                foreach (UndecoratedFunction ctor in kvp.Value)
                {
                    // This is a workaround for an unknown parsing bug.
                    // Some ctors are parsed with no args list.
                    if (ctor.ArgTypes == null)
                        continue;

                    // TODO: Expend to ctors with multiple args
                    // NOTE: args are 0 but the 'this' argument is implied (Usually in ecx. Decompilers shows it as the first argument)
                    if (ctor.NumArgs > 4)
                        continue;


                    if (_alreadyHookedDecorated.Contains(ctor.DecoratedName))
                    {
                        // Logger.Debug($"[WARNING] Attempted re-hooking of ctor. UnDecorated: {ctor.UndecoratedFullName} , Decorated: {ctor.DecoratedName}");
                        continue;
                    }
                    _alreadyHookedDecorated.Add(ctor.DecoratedName);


                    if (ctor.NumArgs > 1)
                    {
                        Debug.WriteLine($"[{nameof(MsvcOffensiveGC)}] Skipping hooking CTOR {ctor.UndecoratedFullName} with {ctor.NumArgs} args");
                        continue;
                    }

                    // Logger.Debug($"[{nameof(MsvcOffensiveGC)}] Hooking CTOR {ctor.UndecoratedFullName} with {ctor.NumArgs} args");
                    DetoursNetWrapper.Instance.AddHook(type, ctor, UnifiedCtor, HarmonyPatchPosition.Prefix);
                    // Logger.Debug($"[{nameof(MsvcOffensiveGC)}] QUICK EXIT");
                    attemptedHookedCtorsCount++;
                }
            }

            // Logger.Debug($"[{nameof(MsvcOffensiveGC)}] Done hooking ctors. attempted to hook: {attemptedHookedCtorsCount} ctors");
            // Logger.Debug($"[{nameof(MsvcOffensiveGC)}] Done hooking ctors. DelegateStore.Mine.Count: {DelegateStore.Mine.Count}");
        }
        public static bool UnifiedCtor(object selfObj, object[] args, ref object retValue)
        {
            if (selfObj is NativeObject self)
            {
                // Logger.Debug($"[UnifiedCtor] self.TypeInfo.Name: {self.TypeInfo.Name}, Addr: 0x{self.Address:x16}");
                RegisterClass(self.Address, self.TypeInfo.ModuleName, self.TypeInfo.Name);
                TryMatchClassToSize(self.Address, self.TypeInfo.FullTypeName);
            }
            else
            {
                // Logger.Debug($"[UnifiedCtor] Args: {args.Length}, Self: <ERROR!>");
            }
            return true; // Call Original
        }

        // DTORS
        private static void HookDtors(Dictionary<TypeInfo, List<UndecoratedFunction>> dtors)
        {
            // Hook all ctors
            // Logger.Debug($"[{nameof(MsvcOffensiveGC)}] Starting to hook dtors...");
            int attemptedHookedCtorsCount = 0;
            foreach (var kvp in dtors)
            {
                TypeInfo type = kvp.Key;
                string fullTypeName = $"{type.ModuleName}!{type.Name}";
                foreach (UndecoratedFunction dtor in kvp.Value)
                {
                    // NOTE: args are 0 but the 'this' argument is implied (Usually in ecx. Decompilers shows it as the first argument)
                    if (dtor.NumArgs > 4)
                        continue;


                    if (_alreadyHookedDecorated.Contains(dtor.DecoratedName))
                    {
                        // Logger.Debug($"[WARNING] Attempted re-hooking of dtor. UnDecorated: {dtor.UndecoratedFullName} , Decorated: {dtor.DecoratedName}");
                        continue;
                    }
                    _alreadyHookedDecorated.Add(dtor.DecoratedName);


                    if (dtor.NumArgs > 1)
                    {
                        // Logger.Debug($"[{nameof(MsvcOffensiveGC)}] Skipping hooking DTOR {dtor.UndecoratedFullName} with {dtor.NumArgs} args");
                        continue;
                    }

                    DetoursNetWrapper.Instance.AddHook(type, dtor, UnifiedDtor, HarmonyPatchPosition.Prefix);
                    attemptedHookedCtorsCount++;
                    // Logger.Debug($"[{nameof(MsvcOffensiveGC)}] DTOR hooked! {dtor.UndecoratedFullName} !~!~!~!~!~!~!");

                }
            }

            // Logger.Debug($"[{nameof(MsvcOffensiveGC)}] Done hooking dtors. attempted to hook: {attemptedHookedCtorsCount} ctors");
            // Logger.Debug($"[{nameof(MsvcOffensiveGC)}] Done hooking dtors. DelegateStore.Mine.Count: {DelegateStore.Mine.Count}");
        }
        public static bool UnifiedDtor(object selfObj, object[] args, ref object retValue)
        {
            if (selfObj is NativeObject self)
            {
                DeregisterClass(self.Address, self.TypeInfo.ModuleName, self.TypeInfo.Name);

                // Intercept dtors here to prevent de-allocation
                if (_frozenObjectsToDtorUpdateActions.TryGetValue(self.Address, out var dtorUpdateAction))
                {
                    // Logger.Debug($"[UnifiedDtor] DING DING DING! Frozen object flow! Avoiding DTOR :) Address: 0x{self.Address:X16}");
                    dtorUpdateAction.Invoke(self.Address, self.TypeInfo);
                    return false; // Skip Original

                }
            }
            else
            {
                // Logger.Debug($"[UnifiedDtor] error Args: {args.Length}, Self: <ERROR!>");
            }
            return true; // Call Original
        }

        // AUTO CLASS INIT 2
        private void HookAutoClassInit2Funcs(Dictionary<TypeInfo, UndecoratedFunction> initMethods)
        {
            // Hook all __autoclassinit2
            // Logger.Debug($"[{nameof(MsvcOffensiveGC)}] Starting to hook __autoclassinit2. Count: {initMethods.Count}");
            foreach (var kvp in initMethods)
            {
                UndecoratedFunction autoClassInit2 = kvp.Value;
                // Logger.Debug($"[{nameof(MsvcOffensiveGC)}] Hooking {autoClassInit2.UndecoratedFullName}");
                DetoursNetWrapper.Instance.AddHook(kvp.Key, kvp.Value, UnifiedAutoClassInit2, HarmonyPatchPosition.Prefix);
            }

            // Logger.Debug($"[{nameof(MsvcOffensiveGC)}] Done hooking __autoclassinit2.");
            // Logger.Debug($"[{nameof(MsvcOffensiveGC)}] DelegateStore.Mine.Count: {DelegateStore.Mine.Count}");
        }

        private bool UnifiedAutoClassInit2(object selfObj, object[] args, ref object retvalue)
        {
            if (selfObj is NativeObject self)
            {
                // Logger.Debug($"[UnifiedAutoClassInit2] Secret: {self.TypeInfo.Name}, Args: {args.Length}");
                nuint size = (nuint)args.FirstOrDefault();
                // Logger.Debug($"[UnifiedAutoClassInit2] Secret: {self.TypeInfo.Name}, Args: {args.Length}, Self: 0x{size:x16}");
                lock (_classSizesLock)
                {
                    // Found a new match!
                    _classSizes[self.TypeInfo.FullTypeName] = size;
                }
            }

            else
            {
                // Logger.Debug($"[UnifiedAutoClassInit2] Args: {args.Length}, Self: <ERROR!>");
            }

            return true; // Don't skip original
        }


        // +-----------------+
        // | Object Tracking |
        // +-----------------+

        /// <summary>
        /// Indicate a specific class instance was allocated at a given address.
        /// </summary>
        public static void RegisterClass(nuint address, string moduleName, string className)
        {
            lock (_classToInstancesLock)
            {
                if (!_moduleToClasses.TryGetValue(moduleName, out var classToInstances))
                {
                    classToInstances = new NamedDict<HashSet<nuint>>();
                    _moduleToClasses[moduleName] = classToInstances;
                }
                if (!classToInstances.TryGetValue(className, out var instancesList))
                {
                    instancesList = new HashSet<nuint>();
                    classToInstances[className] = instancesList;
                }
                instancesList.Add(address);
            }
        }

        /// <summary>
        /// Indicate a specific class instance was destroyed at a given address.
        /// </summary>
        public static void DeregisterClass(nuint address, string moduleName, string className)
        {
            lock (_classToInstancesLock)
            {
                if (!_moduleToClasses.TryGetValue(moduleName, out var classToInstances))
                    return;
                if (!classToInstances.TryGetValue(className, out var instancesList))
                    return;
                instancesList.Remove(address);
            }
        }

        // +---------------------------+
        // | Class Sizes Match  Making |
        // +---------------------------+

        /// <summary>
        /// Indicate a specific size was allocated at a given address.
        /// </summary>
        public static void RegisterSize(nuint address, nuint size)
        {
            lock (_addressToSizeLock)
            {
                _addressToSize.AddOrUpdate(address, size);
                //// Logger.Debug($"[RegisterSize] Addr: 0x{address:x16}, Size: {size}");
            }
        }

        /// <summary>
        /// Given an address and the name of the class initalized there, check if a size was registered for that address.
        /// If so, record that match.
        /// </summary>
        public static void TryMatchClassToSize(nuint address, string fullTypeClassName)
        {
            // Check recent "allocations"
            bool res;
            nuint size;
            lock (_addressToSizeLock)
            {
                res = _addressToSize.Remove(address, out size);
            }
            // Logger.Debug($"[TryMatchClassToSize] fullTypeClassName: {fullTypeClassName}, Addr: 0x{address:x16}, Results: {res}");

            // Check if we already found the size of this class
            lock (_classSizesLock)
            {
                if (_classSizes.ContainsKey(fullTypeClassName))
                {
                    // Logger.Debug($"[TryMatchClassToSize] Already matched size of fullTypeClassName: {fullTypeClassName}");
                    return;
                }

                if (res)
                {
                    // Found a new match!
                    _classSizes[fullTypeClassName] = size;
                    // Logger.Debug($"[TryMatchClassToSize] Found size of class. Full Name: {fullTypeClassName}, Size: {size} bytes");
                }
            }
        }

        private static ConcurrentDictionary<ulong, Action<ulong, TypeInfo>> _frozenObjectsToDtorUpdateActions = new();

        // Pinning
        public void Pin(ulong objAddress, Action<ulong, TypeInfo> dtorUpdator)
        {
            // Set the DTORs registeration function aside so any dtor can register itself on-demand
            _frozenObjectsToDtorUpdateActions[objAddress] = dtorUpdator;

            // Ask the C++ Helper to watch for `free` calls to our address
            MsvcOffensiveGcHelper.AddAddress((IntPtr)objAddress);
        }

        // Unpinning
        public void Unpin(ulong objAddress)
        {
            _frozenObjectsToDtorUpdateActions.Remove(objAddress, out _);
            MsvcOffensiveGcHelper.RemoveAddress((IntPtr)objAddress);
            // TODO: Invoke dtors + free here?
        }
    }
}
