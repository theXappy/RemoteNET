using ScubaDiver.API.Interactions.Dumps;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using ScubaDiver.API.Hooking;
using ScubaDiver.Demangle.Demangle;
using ScubaDiver.Demangle.Demangle.Core.Serialization;
using ScubaDiver.Hooking;
using System.Reflection.Emit;
using DetoursNet;
using System.Threading;
using ScubaDiver.Rtti;
using TypeInfo = ScubaDiver.Rtti.TypeInfo;
using System.Net.Sockets;
using NtApiDotNet.Ndr.Marshal;
using System.Runtime.InteropServices;

namespace ScubaDiver
{
    internal class MsvcOffensiveGC
    {

        // From https://github.com/gperftools/gperftools/issues/715
        // [x64] operator new(ulong size)
        private string kMangledNew64 = "??2@YAPEAX_K@Z";
        // [x64] operator new(ulong size, struct std::nothrow_t const &obj)
        private string kMangledNewNothrow64 = "??2@YAPEAX_KAEBUnothrow_t@std@@@z";

        private string operatorNewName = "operator new";

        public void Init(List<UndecoratedModule> modules)
        {
            Logger.Debug($"[{nameof(MsvcOffensiveGC)}] {nameof(Init)} IN");

            Dictionary<long, UndecoratedFunction> initMethods = GetAutoClassInit2Funcs(modules);
            Dictionary<TypeInfo, List<UndecoratedFunction>> ctors = GetCtors(modules);
            Dictionary<string, List<UndecoratedFunction>> newOperators = GetNewOperators(modules);

            HookAutoClassInit2Funcs(initMethods);
            HookCtors(ctors);
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
                foreach (TypeInfo type in module.Type)
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

        private Dictionary<long, UndecoratedFunction> GetAutoClassInit2Funcs(List<UndecoratedModule> modules)
        {
            void ProcessSingleModule(UndecoratedModule module,
                Dictionary<long, UndecoratedFunction> workingDict)
            {
                foreach (TypeInfo type in module.Type)
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

                    DetoursNetWrapper.Instance.AddHook(newOperator, UnifiedOperatorNew);
                }
            }

            Logger.Debug(
                $"[{nameof(MsvcOffensiveGC)}] Done hooking 'operator new's. attempted to hook: {attemptedOperatorNews} funcs");
            Logger.Debug(
                $"[{nameof(MsvcOffensiveGC)}] Done hooking 'operator new's.. DelegateStore.Mine.Count: {DelegateStore.Mine.Count}");
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
                        Logger.Debug($"[WARNING] Attempted re-hooking of ctor. UnDecorated: {ctor.UndecoratedName} , Decorated: {ctor.DecoratedName}");
                        continue;
                    }
                    _alreadyHookedDecorated.Add(ctor.DecoratedName);


                    if (ctor.NumArgs > 1 || !ctor.UndecoratedName.StartsWith("SPen"))
                    {
                        Debug.WriteLine($"[{nameof(MsvcOffensiveGC)}] Skipping hooking CTOR {ctor.UndecoratedName} with {ctor.NumArgs} args");
                        continue;
                    }

                    Console.WriteLine($"[{nameof(MsvcOffensiveGC)}] Hooking CTOR {ctor.UndecoratedName} with {ctor.NumArgs} args");
                    DetoursNetWrapper.Instance.AddHook(ctor, UnifiedCtor);
                    Console.WriteLine($"[{nameof(MsvcOffensiveGC)}] QUICK EXIT");
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
            return;


            // Hook all __autoclassinit2
            Logger.Debug($"[{nameof(MsvcOffensiveGC)}] Starting to hook __autoclassinit2. Count: {initMethods.Count}");
            foreach (var kvp in initMethods)
            {
                UndecoratedFunction autoClassInit2 = kvp.Value;
                Logger.Debug($"[{nameof(MsvcOffensiveGC)}] Hooking {autoClassInit2.UndecoratedName}");
                DetoursNetWrapper.Instance.AddHook(kvp.Value, UnifiedAutoClassInit2);
            }

            Logger.Debug($"[{nameof(MsvcOffensiveGC)}] Done hooking __autoclassinit2.");
            Logger.Debug($"[{nameof(MsvcOffensiveGC)}] DelegateStore.Mine.Count: {DelegateStore.Mine.Count}");
        }


        // +---------------+
        // | Ctors Hooking |
        // +---------------+

        public static bool UnifiedCtor(DetoursMethodGenerator.DetoursTrampoline secret, object[] args, out nuint overridenReturnValue)
        {
            overridenReturnValue = 0;

            //Console.WriteLine($"[UnifiedCtor] Secret: {secret}, Args: {args.Length}");
            object first = args.FirstOrDefault();
            if (first is nuint self)
            {
                //Console.WriteLine($"[UnifiedCtor] Secret: {secret}, Args: {args.Length}, Self: 0x{self:x16}");
                RegisterClassName(self, secret.Name);
            }
            else
            {
                Console.WriteLine($"[UnifiedCtor] Secret: {secret}, Args: {args.Length}, Self: <ERROR!>");
            }
            return true;
        }

        // +--------------------------+
        // | __autoclassinit2 Hooking |
        // +--------------------------+

        //public static void UnifiedAutoClassInit2(string secret, ulong self, ulong size)
        //{
        //    // NOTE: Secret is not to be trusted here because __autoclassinit2 is often shared
        //    // between various classes in the same dll.
        //    RegisterSize(self, size);

        //    if (!_cached.ContainsKey(secret)) return;
        //    var (originalHookMethodInfo, _) = _cached[secret];

        //    if (!DelegateStore.Real.ContainsKey(originalHookMethodInfo)) return;
        //    var originalMethod = (AutoClassInit2Type)DelegateStore.Real[originalHookMethodInfo];
        //    // Invoking original ctor
        //    originalMethod(self, size);
        //}
        public static bool UnifiedAutoClassInit2(DetoursMethodGenerator.DetoursTrampoline secret, object[] args, out nuint overridenReturnValue)
        {
            overridenReturnValue = 0;

            Console.WriteLine($"[UnifiedAutoClassInit2] Secret: {secret.Name}, Args: {args.Length}");
            object first = args.FirstOrDefault();
            Console.WriteLine($"[UnifiedAutoClassInit2] Secret: {secret.Name}, Args: {args.Length}, Self: {(first is nuint ? $"0x{first:x16}" : "<ERROR!>")}");
            object second = args.FirstOrDefault();
            Console.WriteLine($"[UnifiedAutoClassInit2] Secret: {secret.Name}, Args: {args.Length}, Size: {(second is nuint ? $"0x{second:x16}" : "<ERROR!>")}");
            return true;
        }


        // +----------------------+
        // | Operator new Hooking |
        // +----------------------+

        //public static ulong UnifiedOperatorNew(string secret, ulong size)
        //{
        //    ulong res = 0;

        //    if (!_cached.ContainsKey(secret))
        //        return res;
        //    var (originalHookMethodInfo, _) = _cached[secret];

        //    var originalMethod = (OperatorNewType)DelegateStore.Real[originalHookMethodInfo];
        //    // Invoking original ctor
        //    res = originalMethod(size);
        //    //Logger.Debug($"[OperatorNew] Invoked original. Size: {size}, returned addr: {res}");

        //    if (res != 0)
        //    {
        //        RegisterSize(res, size);
        //    }
        //    else
        //    {
        //        Logger.Debug("[Error] Operator new failed (returned null).");
        //    }

        //    return res;
        //}
        private delegate nuint OperatorNewType(nuint size);

        public static bool UnifiedOperatorNew(DetoursMethodGenerator.DetoursTrampoline trampoline, object[] args, out nuint overridenReturnValue)
        {
            overridenReturnValue = 0;

            //Console.WriteLine($"[UnifiedOperatorNew] Secret: {secret}, Args: {args.Length}");
            object first = args.FirstOrDefault();
            if (first is nuint size)
            {
                // Calling original
                OperatorNewType opNew = trampoline.GetRealMethod<OperatorNewType>();

                // Invoking original ctor
                overridenReturnValue = opNew(size);
                if(overridenReturnValue != 0)
                    RegisterSize(overridenReturnValue, size);
                return false;
            }
            else
            {
                Console.WriteLine(
                    $"[UnifiedOperatorNew] Secret: {trampoline.Name}, Args: {args.Length}, Size: <ERROR!>");
            }

            return true;
        }

        // +---------------------------+
        // | Class Sizes Match  Making |
        // +---------------------------+
        private static LRUCache<nuint, nuint> _addrToSize = new LRUCache<nuint, nuint>(100);
        private static object _addrToSizeLock = new();
        private static Dictionary<string, nuint> ClassSizes = new Dictionary<string, nuint>();
        private static object ClassSizesLock = new();
        public IReadOnlyDictionary<string, nuint> GetFindings() => ClassSizes;

        public static void RegisterSize(nuint addr, nuint size)
        {
            lock (_addrToSizeLock)
            {
                _addrToSize.AddOrUpdate(addr, size);
            }
        }

        public static void RegisterClassName(nuint addr, string className)
        {
            // Check if we already found the size of this class
            lock (ClassSizesLock)
            {
                if (ClassSizes.ContainsKey(className))
                    return;
            }

            // Check recent "allocations"
            bool res;
            nuint size;
            lock (_addrToSizeLock)
            {
                res = _addrToSize.TryGetValue(addr, true, out size);
            }

            if (res)
            {
                // Found a match!
                ClassSizes[className] = size;
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
            value = default(TValue);
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
