using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using ScubaDiver.Hooking;
using DetoursNet;
using ScubaDiver.API.Hooking;
using ScubaDiver.Rtti;
using ScubaDiver.Demangle.Demangle.Core;

namespace ScubaDiver
{
    class NamedDict<T> : Dictionary<string, T>
    {
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

        // Size Match-Making
        private static readonly object _addressToSizeLock = new();
        private static readonly LRUCache<nuint, nuint> _addressToSize = new(100);
        private static readonly object _classSizesLock = new();
        private static readonly NamedDict<nuint> _classSizes = new();
        public IReadOnlyDictionary<string, nuint> ClassSizes() => _classSizes;

        public void Init(List<UndecoratedModule> modules)
        {
            Logger.Debug($"[{nameof(MsvcOffensiveGC)}] {nameof(Init)} IN");

            Dictionary<long, UndecoratedFunction> initMethods = GetAutoClassInit2Funcs(modules);
            Dictionary<TypeInfo, List<UndecoratedFunction>> ctors = GetCtors(modules);
            Dictionary<TypeInfo, List<UndecoratedFunction>> dtors = GetDtors(modules);
            Dictionary<string, List<UndecoratedFunction>> newOperators = GetNewOperators(modules);

            HookAutoClassInit2Funcs(initMethods);
            HookCtors(ctors);
            HookDtors(dtors);
            HookNewOperators(newOperators);

            Logger.Debug($"[{nameof(MsvcOffensiveGC)}] {nameof(Init)} OUT");
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
        private Dictionary<long, UndecoratedFunction> GetAutoClassInit2Funcs(List<UndecoratedModule> modules)
        {
            void ProcessSingleModule(UndecoratedModule module,
                Dictionary<long, UndecoratedFunction> workingDict)
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
                    workingDict[func.Address] = func;
                }
            }

            Dictionary<long, UndecoratedFunction> res = new();
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
        private static bool UnifiedOperatorNew(object unused, object[] args, ref object retValue)
        {
            object first = args.FirstOrDefault();
            if (first is nuint size && retValue is nuint retNuint)
            {
                nuint allocatedObjAddress = retNuint;
                RegisterSize(allocatedObjAddress, size);
            }
            else
            {
                Console.WriteLine(
                    $"[UnifiedOperatorNew] Args: {args.Length}, Size: <ERROR!>");
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

                    Console.WriteLine($"[{nameof(MsvcOffensiveGC)}] Hooking CTOR {ctor.UndecoratedFullName} with {ctor.NumArgs} args");
                    DetoursNetWrapper.Instance.AddHook(type, ctor, UnifiedCtor, HarmonyPatchPosition.Prefix);
                    Console.WriteLine($"[{nameof(MsvcOffensiveGC)}] QUICK EXIT");
                    attemptedHookedCtorsCount++;
                }
            }

            Logger.Debug(
                $"[{nameof(MsvcOffensiveGC)}] Done hooking ctors. attempted to hook: {attemptedHookedCtorsCount} ctors");
            Logger.Debug(
                $"[{nameof(MsvcOffensiveGC)}] Done hooking ctors. DelegateStore.Mine.Count: {DelegateStore.Mine.Count}");
        }
        public static bool UnifiedCtor(object unused, object[] args, ref object retValue)
        {
            object first = args.FirstOrDefault();
            if (first is NativeObject self)
            {
                RegisterClass(self.Address, self.TypeInfo.ModuleName, self.TypeInfo.Name);
                TryMatchClassToSize(self.Address, self.TypeInfo.Name);
            }
            else
            {
                Console.WriteLine($"[UnifiedCtor] Args: {args.Length}, Self: <ERROR!>");
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
                Logger.Debug($"[UnifiedDtor] Secret: {self.TypeInfo.Name}, Args: {args.Length}, Self: 0x{self.Address:x16}");

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
        private void HookAutoClassInit2Funcs(Dictionary<long, UndecoratedFunction> initMethods)
        {
            Logger.Debug($"[{nameof(MsvcOffensiveGC)}] HookAutoClassInit2Funcs DISABLED!");

            // Hook all __autoclassinit2
            //Logger.Debug($"[{nameof(MsvcOffensiveGC)}] Starting to hook __autoclassinit2. Count: {initMethods.Count}");
            //foreach (var kvp in initMethods)
            //{
            //    UndecoratedFunction autoClassInit2 = kvp.Value;
            //    Logger.Debug($"[{nameof(MsvcOffensiveGC)}] Hooking {autoClassInit2.UndecoratedFullName}");
            //    DetoursNetWrapper.Instance.AddHook(kvp.Value, UnifiedAutoClassInit2);
            //}

            //Logger.Debug($"[{nameof(MsvcOffensiveGC)}] Done hooking __autoclassinit2.");
            //Logger.Debug($"[{nameof(MsvcOffensiveGC)}] DelegateStore.Mine.Count: {DelegateStore.Mine.Count}");
        }
        public static bool UnifiedAutoClassInit2(DetoursMethodGenerator.DetouredFuncInfo secret, object[] args, out nuint overriddenReturnValue)
        {
            overriddenReturnValue = 0;

            Console.WriteLine($"[UnifiedAutoClassInit2] Secret: {secret.Name}, Args: {args.Length}");
            object first = args.FirstOrDefault();
            Console.WriteLine($"[UnifiedAutoClassInit2] Secret: {secret.Name}, Args: {args.Length}, Self: {(first is nuint ? $"0x{first:x16}" : "<ERROR!>")}");
            object second = args.FirstOrDefault();
            Console.WriteLine($"[UnifiedAutoClassInit2] Secret: {secret.Name}, Args: {args.Length}, Size: {(second is nuint ? $"0x{second:x16}" : "<ERROR!>")}");

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
                if (!_moduleToClasses.TryGetValue(className, out var classToInstances))
                {
                    classToInstances = new NamedDict<HashSet<nuint>>();
                    _moduleToClasses[className] = classToInstances;
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
                if (!_moduleToClasses.TryGetValue(className, out var classToInstances))
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
            }
        }

        /// <summary>
        /// Given an address and the name of the class initalized there, check if a size was registered for that address.
        /// If so, record that match.
        /// </summary>
        public static void TryMatchClassToSize(nuint address, string className)
        {
            // Check if we already found the size of this class
            lock (_classSizesLock)
            {
                if (_classSizes.ContainsKey(className))
                    return;
            }

            // Check recent "allocations"
            bool res;
            nuint size;
            lock (_addressToSizeLock)
            {
                res = _addressToSize.TryGetValue(address, true, out size);
            }

            if (res)
            {
                // Found a match!
                _classSizes[className] = size;
                Logger.Debug($"[MsvcOffensiveGC][MatchMaking] Found size of class. Name: {className}, Size: {size} bytes");
            }
        }
    }
}
