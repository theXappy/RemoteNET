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
    internal class MsvcOffensiveGC
    {
        // From https://github.com/gperftools/gperftools/issues/715
        // [x64] operator new(ulong size)
        //private string kMangledNew64 = "??2@YAPEAX_K@Z";
        // [x64] operator new(ulong size, struct std::nothrow_t const &obj)
        //private string kMangledNewNothrow64 = "??2@YAPEAX_KAEBUnothrow_t@std@@@z";

        private string operatorNewName = "operator new";

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

        private static bool UnifiedOperatorNew(object instance, object[] args, ref object retValue)
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

        private static HashSet<string> _alreadyHookedDecorated = new HashSet<string>();
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

                    DetoursNetWrapper.Instance.AddHook(type,  dtor, UnifiedDtor, HarmonyPatchPosition.Prefix);
                    attemptedHookedCtorsCount++;
                }
            }

            Logger.Debug(
                $"[{nameof(MsvcOffensiveGC)}] Done hooking ctors. attempted to hook: {attemptedHookedCtorsCount} ctors");
            Logger.Debug(
                $"[{nameof(MsvcOffensiveGC)}] Done hooking ctors. DelegateStore.Mine.Count: {DelegateStore.Mine.Count}");
        }

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


        // +---------------+
        // | Ctors Hooking |
        // +---------------+

        public static bool UnifiedCtor(object instance, object[] args, ref object retValue)
        {
            object first = args.FirstOrDefault();
            if (first is NativeObject self)
            {
                RegisterClassName(self.Address, self.TypeInfo.Name);
            }
            else
            {
                Console.WriteLine($"[UnifiedCtor] Args: {args.Length}, Self: <ERROR!>");
            }
            return false;
        }
        public static bool UnifiedDtor(object instance, object[] args, ref object retValue)
        {
            if (instance is NativeObject self)
            {
                Console.WriteLine($"[UnifiedDtor] Secret: {self.TypeInfo.Name}, Args: {args.Length}, Self: 0x{self.Address:x16}");
            }
            else
            {
                Console.WriteLine($"[UnifiedDtor] error Args: {args.Length}, Self: <ERROR!>");
            }
            return false; // Skip original
        }


        // +--------------------------+
        // | __autoclassinit2 Hooking |
        // +--------------------------+

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


        // +----------------------+
        // | Operator new Hooking |
        // +----------------------+

        private delegate nuint OperatorNewType(nuint size);

        // +---------------------------+
        // | Class Sizes Match  Making |
        // +---------------------------+
        private static readonly LRUCache<nuint, nuint> _addressToSize = new LRUCache<nuint, nuint>(100);
        private static readonly object _addressToSizeLock = new();
        private static readonly Dictionary<string, nuint> _classSizes = new Dictionary<string, nuint>();
        private static readonly object ClassSizesLock = new();
        public IReadOnlyDictionary<string, nuint> GetFindings() => _classSizes;

        public static void RegisterSize(nuint address, nuint size)
        {
            lock (_addressToSizeLock)
            {
                _addressToSize.AddOrUpdate(address, size);
            }
        }

        public static void RegisterClassName(nuint address, string className)
        {
            // Check if we already found the size of this class
            lock (ClassSizesLock)
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
    public class LRUCache<TKey, TValue>
    {
        private readonly int capacity;
        private readonly Dictionary<TKey, LinkedListNode<CacheItem>> cache;
        private readonly LinkedList<CacheItem> lruList;

        public LRUCache(int capacity)
        {
            this.capacity = capacity;
            cache = new Dictionary<TKey, LinkedListNode<CacheItem>>(capacity);
            lruList = new LinkedList<CacheItem>();
        }

        public void AddOrUpdate(TKey key, TValue value)
        {
            if (cache.ContainsKey(key))
            {
                // If the key is already in the cache, update its value and move it to the front of the LRU list
                LinkedListNode<CacheItem> node = cache[key];
                node.Value.Value = value;
                lruList.Remove(node);
                lruList.AddFirst(node);
            }
            else
            {
                // If the key is not in the cache, create a new node and add it to the cache and the front of the LRU list
                LinkedListNode<CacheItem> node = new LinkedListNode<CacheItem>(new CacheItem(key, value));
                cache.Add(key, node);
                lruList.AddFirst(node);

                // If the cache exceeds the capacity, remove the least recently used item from the cache and the LRU list
                if (cache.Count > capacity)
                {
                    LinkedListNode<CacheItem> lastNode = lruList.Last;
                    cache.Remove(lastNode.Value.Key);
                    lruList.RemoveLast();
                }
            }
        }

        public bool TryGetValue(TKey key, bool delete, out TValue value)
        {
            if (cache.TryGetValue(key, out LinkedListNode<CacheItem> node))
            {
                // If the key is found in the cache, move it to the front of the LRU list and return its value
                lruList.Remove(node);
                if (delete)
                {
                    cache.Remove(key);
                }
                else
                {
                    lruList.AddFirst(node);
                }

                value = node.Value.Value;
                return true;
            }

            // If the key is not found in the cache, return default value for TValue
            value = default;
            return false;
        }

        private class CacheItem
        {
            public TKey Key { get; }
            public TValue Value { get; set; }

            public CacheItem(TKey key, TValue value)
            {
                Key = key;
                Value = value;
            }
        }
    }
}
