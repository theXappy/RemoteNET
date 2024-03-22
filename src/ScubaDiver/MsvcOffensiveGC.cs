using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using ScubaDiver.Hooking;
using DetoursNet;
using ScubaDiver.API.Hooking;
using ScubaDiver.Rtti;
using ScubaDiver.Demangle.Demangle.Core;
using ScubaDiver.Demangle.Demangle.Core.Types;
using System.Net;
using System.Drawing;

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

    internal class MsvcOffensiveGC
    {
        // From https://github.com/gperftools/gperftools/issues/715
        // [x64] operator new(ulong size)
        //private string kMangledNew64 = "??2@YAPEAX_K@Z";
        // [x64] operator new(ulong size, struct std::nothrow_t const &obj)
        //private string kMangledNewNothrow64 = "??2@YAPEAX_KAEBUnothrow_t@std@@@z";

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
        private static readonly LimitedSizeDictionary<nuint, nuint> _addressToSize = new(100);
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

        public void HookModule(UndecoratedModule module) => HookModules(new List<UndecoratedModule>() { module });

        private List<UndecoratedModule> _alreadyHookedModules = new List<UndecoratedModule>();

        public void HookModules(List<UndecoratedModule> modules)
        {
            Logger.Debug($"[{nameof(MsvcOffensiveGC)}] {nameof(HookModules)} IN");

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

            Logger.Debug($"[{nameof(MsvcOffensiveGC)}] {nameof(HookModules)} OUT");
        }

        private Dictionary<string, List<UndecoratedFunction>> GetNewOperators(List<UndecoratedModule> modules)
        {
            void ProcessSingleModule(UndecoratedModule module,
                Dictionary<string, List<UndecoratedFunction>> workingDict)
            {
                if (module.TryGetTypelessFunc(operatorNewName, out var methodGroup))
                {
                    Logger.Debug($"[{nameof(MsvcOffensiveGC)}] FOUND {operatorNewName} in {module.Name}");
                    workingDict[module.Name] = methodGroup;
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
                string className = fullTypeName.Substring(fullTypeName.LastIndexOf("::") + 2);
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
                string className = fullTypeName.Substring(fullTypeName.LastIndexOf("::") + 2);
                return $"{fullTypeName}::~{className}";
            }

            void ProcessSingleModule(UndecoratedModule module,
                Dictionary<TypeInfo, List<UndecoratedFunction>> workingDict)
            {
                foreach (TypeInfo type in module.Types)
                {
                    string ctorName = GetDtorName(type);
                    if (!module.TryGetTypeFunc(type, ctorName, out var dtors))
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
                        Logger.Debug($"Expected exactly one __autoclassinit2 function for type {type.Name}, Found {methodGroup.Count}");
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
            Logger.Debug($"[{nameof(MsvcOffensiveGC)}] Starting to hook 'operator new's...");
            int attemptedOperatorNews = 0;
            foreach (var moduleToFuncs in newOperators)
            {
                foreach (var newOperator in moduleToFuncs.Value)
                {
                    attemptedOperatorNews++;
                    Logger.Debug(
                        $"[{nameof(MsvcOffensiveGC)}] Hooking 'operator new' at 0x{newOperator.Address:x16} ({newOperator.Module})");

                    //DetoursNetWrapper.Instance.AddHook(newOperator, UnifiedOperatorNew);
                    DetoursNetWrapper.Instance.AddHook(TypeInfo.Dummy, newOperator, UnifiedOperatorNew, HarmonyPatchPosition.Postfix);
                }
            }

            Logger.Debug(
                $"[{nameof(MsvcOffensiveGC)}] Done hooking 'operator new' s. attempted to hook: {attemptedOperatorNews} funcs");
            Logger.Debug(
                $"[{nameof(MsvcOffensiveGC)}] Done hooking 'operator new' s. DelegateStore.Mine.Count: {DelegateStore.Mine.Count}");
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
                Logger.Debug($"[UnifiedOperatorNew] sizeObj: {sizeObj}");
            }

            return false; // Don't skip original
        }
        private delegate nuint OperatorNewType(nuint size);

        // CTORs
        private static void HookCtors(Dictionary<TypeInfo, List<UndecoratedFunction>> ctors)
        {
            // Hook all ctors
            Logger.Debug($"[{nameof(MsvcOffensiveGC)}] Starting to hook ctors...");
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
                        Logger.Debug($"[WARNING] Attempted re-hooking of ctor. UnDecorated: {ctor.UndecoratedFullName} , Decorated: {ctor.DecoratedName}");
                        continue;
                    }
                    _alreadyHookedDecorated.Add(ctor.DecoratedName);


                    if (ctor.NumArgs > 1 || !ctor.UndecoratedFullName.StartsWith("SPen"))
                    {
                        Debug.WriteLine($"[{nameof(MsvcOffensiveGC)}] Skipping hooking CTOR {ctor.UndecoratedFullName} with {ctor.NumArgs} args");
                        continue;
                    }

                    Logger.Debug($"[{nameof(MsvcOffensiveGC)}] Hooking CTOR {ctor.UndecoratedFullName} with {ctor.NumArgs} args");
                    DetoursNetWrapper.Instance.AddHook(type, ctor, UnifiedCtor, HarmonyPatchPosition.Prefix);
                    Logger.Debug($"[{nameof(MsvcOffensiveGC)}] QUICK EXIT");
                    attemptedHookedCtorsCount++;
                }
            }

            Logger.Debug(
                $"[{nameof(MsvcOffensiveGC)}] Done hooking ctors. attempted to hook: {attemptedHookedCtorsCount} ctors");
            Logger.Debug(
                $"[{nameof(MsvcOffensiveGC)}] Done hooking ctors. DelegateStore.Mine.Count: {DelegateStore.Mine.Count}");
        }
        public static bool UnifiedCtor(object selfObj, object[] args, ref object retValue)
        {
            if (selfObj is NativeObject self)
            {
                Logger.Debug($"[UnifiedCtor] self.TypeInfo.Name: {self.TypeInfo.Name}, Addr: 0x{self.Address:x16}");
                RegisterClass(self.Address, self.TypeInfo.ModuleName, self.TypeInfo.Name);
                TryMatchClassToSize(self.Address, self.TypeInfo.FullTypeName);
            }
            else
            {
                Logger.Debug($"[UnifiedCtor] Args: {args.Length}, Self: <ERROR!>");
            }
            return false;
        }

        // DTORS
        private static void HookDtors(Dictionary<TypeInfo, List<UndecoratedFunction>> dtors)
        {
            // Hook all ctors
            Logger.Debug($"[{nameof(MsvcOffensiveGC)}] Starting to hook dtors...");
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
                        Logger.Debug($"[WARNING] Attempted re-hooking of ctor. UnDecorated: {dtor.UndecoratedFullName} , Decorated: {dtor.DecoratedName}");
                        continue;
                    }
                    _alreadyHookedDecorated.Add(dtor.DecoratedName);


                    if (dtor.NumArgs > 1 || !dtor.UndecoratedFullName.StartsWith("SPen"))
                    {
                        Debug.WriteLine($"[{nameof(MsvcOffensiveGC)}] Skipping hooking CTOR {dtor.UndecoratedFullName} with {dtor.NumArgs} args");
                        continue;
                    }

                    DetoursNetWrapper.Instance.AddHook(type, dtor, UnifiedDtor, HarmonyPatchPosition.Prefix);
                    attemptedHookedCtorsCount++;
                }
            }

            Logger.Debug(
                $"[{nameof(MsvcOffensiveGC)}] Done hooking ctors. attempted to hook: {attemptedHookedCtorsCount} ctors");
            Logger.Debug(
                $"[{nameof(MsvcOffensiveGC)}] Done hooking ctors. DelegateStore.Mine.Count: {DelegateStore.Mine.Count}");
        }
        public static bool UnifiedDtor(object selfObj, object[] args, ref object retValue)
        {
            if (selfObj is NativeObject self)
            {
                // TODO: Intercept dtors here to prevent de-allocation
                DeregisterClass(self.Address, self.TypeInfo.ModuleName, self.TypeInfo.Name);
            }
            else
            {
                Logger.Debug($"[UnifiedDtor] error Args: {args.Length}, Self: <ERROR!>");
            }
            return false; // Skip original
        }

        // AUTO CLASS INIT 2
        private void HookAutoClassInit2Funcs(Dictionary<TypeInfo, UndecoratedFunction> initMethods)
        {
            // Hook all __autoclassinit2
            Logger.Debug($"[{nameof(MsvcOffensiveGC)}] Starting to hook __autoclassinit2. Count: {initMethods.Count}");
            foreach (var kvp in initMethods)
            {
                UndecoratedFunction autoClassInit2 = kvp.Value;
                Logger.Debug($"[{nameof(MsvcOffensiveGC)}] Hooking {autoClassInit2.UndecoratedFullName}");
                DetoursNetWrapper.Instance.AddHook(kvp.Key, kvp.Value, UnifiedAutoClassInit2, HarmonyPatchPosition.Prefix);
            }

            Logger.Debug($"[{nameof(MsvcOffensiveGC)}] Done hooking __autoclassinit2.");
            Logger.Debug($"[{nameof(MsvcOffensiveGC)}] DelegateStore.Mine.Count: {DelegateStore.Mine.Count}");
        }

        private bool UnifiedAutoClassInit2(object selfObj, object[] args, ref object retvalue)
        {
            if (selfObj is NativeObject self)
            {
                Logger.Debug($"[UnifiedAutoClassInit2] Secret: {self.TypeInfo.Name}, Args: {args.Length}");
                nuint size = (nuint)args.FirstOrDefault();
                Logger.Debug(
                    $"[UnifiedAutoClassInit2] Secret: {self.TypeInfo.Name}, Args: {args.Length}, Self: 0x{size:x16}");
                lock (_classSizesLock)
                {
                    // Found a new match!
                    _classSizes[self.TypeInfo.FullTypeName] = size;
                }
            }

            else
            {
                Logger.Debug($"[UnifiedAutoClassInit2] Args: {args.Length}, Self: <ERROR!>");
            }

            return false; // Don't skip original
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
                Logger.Debug($"[RegisterSize] Addr: 0x{address:x16}, Size: {size}");
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
            Logger.Debug($"[TryMatchClassToSize] fullTypeClassName: {fullTypeClassName}, Addr: 0x{address:x16}, Results: {res}");

            // Check if we already found the size of this class
            lock (_classSizesLock)
            {
                if (_classSizes.ContainsKey(fullTypeClassName))
                {
                    Logger.Debug($"[TryMatchClassToSize] Already matched size of fullTypeClassName: {fullTypeClassName}");
                    return;
                }

                if (res)
                {
                    // Found a new match!
                    _classSizes[fullTypeClassName] = size;
                    Logger.Debug($"[TryMatchClassToSize] Found size of class. Full Name: {fullTypeClassName}, Size: {size} bytes");
                }
            }
        }
    }
}
