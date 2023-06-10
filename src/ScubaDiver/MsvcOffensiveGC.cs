﻿using ScubaDiver.API.Interactions.Dumps;
using System;
using System.Collections.Generic;
using System.Linq;
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
            Dictionary<string, List<UndecoratedFunction>> ctors = GetCtors(modules);
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

        private Dictionary<string, List<UndecoratedFunction>> GetCtors(List<UndecoratedModule> modules)
        {
            string GetCtorName(TypeInfo type)
            {
                string fullTypeName = type.Name;
                string className = fullTypeName.Substring(fullTypeName.LastIndexOf("::") + 2);
                return $"{fullTypeName}::{className}";
            }

            void ProcessSingleModule(UndecoratedModule module,
                Dictionary<string, List<UndecoratedFunction>> workingDict)
            {
                foreach (TypeInfo type in module.Type)
                {
                    string ctorName = GetCtorName(type);
                    if (!module.TryGetTypeFunc(type, ctorName, out var ctors))
                        continue;

                    // Found the method group
                    workingDict[type.Name] = ctors;
                }
            }

            Dictionary<string, List<UndecoratedFunction>> res = new();
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

                    var (mi, delegateValue) = GenerateMethodsForSecret(UnifiedOperatorNewMethodInfo, typeof(OperatorNewType),
                        $"{newOperator.Module.Name}!!{newOperator.UndecoratedName}");
                    DetoursNetWrapper.Instance.AddHook(newOperator, HarmonyPatchPosition.Prefix, typeof(OperatorNewType),
                        mi, delegateValue);
                }
            }

            Logger.Debug(
                $"[{nameof(MsvcOffensiveGC)}] Done hooking 'operator new's. attempted to hook: {attemptedOperatorNews} funcs");
            Logger.Debug(
                $"[{nameof(MsvcOffensiveGC)}] Done hooking 'operator new's.. DelegateStore.Mine.Count: {DelegateStore.Mine.Count}");
        }

        private static void HookCtors(Dictionary<string, List<UndecoratedFunction>> ctors)
        {
            // Hook all ctors
            Logger.Debug($"[{nameof(MsvcOffensiveGC)}] Starting to hook ctors...");
            int attemptedHookedCtorsCount = 0;
            foreach (var kvp in ctors)
            {
                foreach (var ctor in kvp.Value)
                {
                    string basicName;
                    SerializedType sig;
                    try
                    {
                        var parser = new MsMangledNameParser(ctor.DecoratedName);
                        (basicName, sig, _) = parser.Parse();
                    }
                    catch (Exception ex)
                    {
                        Logger.Debug($"Failed to demangle ctor of {kvp.Key}, Raw: {ctor.DecoratedName}, Exception: " + ex.Message);
                        continue;
                    }

                    Argument_v1[] args = (sig as SerializedSignature)?.Arguments;
                    if (args == null)
                    {
                        // Failed to parse?!?
                        Logger.Debug($"Failed to parse arguments from ctor of {kvp.Key}, Raw: {ctor.DecoratedName}");
                        continue;
                    }

                    // TODO: Expend to ctors with multiple args
                    // NOTE: args are 0 but the 'this' argument is implied (Usually in ecx. Decompilers shows it as the first argument)
                    if (args.Length == 0)
                    {
                        var (mi, delegateValue) = GenerateMethodsForSecret(UnifiedCtorMethodInfo, typeof(GenericCtorType),
                            ctor.UndecoratedName);
                        DetoursNetWrapper.Instance.AddHook(ctor, HarmonyPatchPosition.Prefix, typeof(GenericCtorType),
                            mi, delegateValue);
                        attemptedHookedCtorsCount++;
                    }
                }
            }

            Logger.Debug(
                $"[{nameof(MsvcOffensiveGC)}] Done hooking ctors. attempted to hook: {attemptedHookedCtorsCount} ctors");
            Logger.Debug(
                $"[{nameof(MsvcOffensiveGC)}] Done hooking ctors. DelegateStore.Mine.Count: {DelegateStore.Mine.Count}");
        }

        private void HookAutoClassInit2Funcs(Dictionary<long, UndecoratedFunction> initMethods)
        {
            // Hook all __autoclassinit2
            Logger.Debug($"[{nameof(MsvcOffensiveGC)}] Starting to hook __autoclassinit2. Count: {initMethods.Count}");
            foreach (var kvp in initMethods)
            {
                Logger.Debug($"[{nameof(MsvcOffensiveGC)}] Hooking {kvp.Value.UndecoratedName}");
                var (mi, delegateValue) = GenerateMethodsForSecret(UnifiedAutoClassInit2MethodInfo, typeof(AutoClassInit2Type), kvp.Value.UndecoratedName);
                DetoursNetWrapper.Instance.AddHook(
                    kvp.Value, HarmonyPatchPosition.Prefix,
                    typeof(AutoClassInit2Type),
                    mi, delegateValue);
            }

            Logger.Debug($"[{nameof(MsvcOffensiveGC)}] Done hooking __autoclassinit2.");
            Logger.Debug($"[{nameof(MsvcOffensiveGC)}] DelegateStore.Mine.Count: {DelegateStore.Mine.Count}");
        }


        // Cache for generated methods (Also used so they aren't GC'd)
        private static Dictionary<string, (MethodInfo, Delegate)> _cached =
            new Dictionary<string, (MethodInfo, Delegate)>();

        public static (MethodInfo, Delegate) GenerateMethodsForSecret(MethodInfo mi, Type delegateType, string secret)
            => GenerateMethodsForSecret(mi.Name, mi.GetParameters().Length - 1, mi.ReturnType, delegateType, secret);

        public static (MethodInfo, Delegate) GenerateMethodsForSecret(string unifiedMethodName, int numArguments, Type retType, Type delegateType,
            string secret)
        {
            if (_cached.TryGetValue(secret, out (MethodInfo, Delegate) existing))
                return existing;

            // Get the method info of the UnifiedMethod
            var unifiedMethodInfo = typeof(MsvcOffensiveGC).GetMethod(unifiedMethodName,
                BindingFlags.Public | BindingFlags.Static);

            Type[] args = Enumerable.Repeat(typeof(ulong), numArguments).ToArray();

            // Create a dynamic method with the desired signature
            var dynamicMethod = new DynamicMethod(
                "GeneratedMethod_" + secret,
                retType,
                args,
                typeof(MsvcOffensiveGC)
            );

            // Generate IL for the dynamic method
            var ilGenerator = dynamicMethod.GetILGenerator();
            ilGenerator.Emit(OpCodes.Ldstr, secret); // Load the secret string onto the stack
            // Load OUR args onto the stack as args to the unified method
            for (int i = 0; i < numArguments; i++)
                ilGenerator.Emit(OpCodes.Ldarg, i);

            ilGenerator.Emit(OpCodes.Call, unifiedMethodInfo); // Call the UnifiedMethod
            ilGenerator.Emit(OpCodes.Ret); // Return from the method

            // Create a delegate for the dynamic method
            Delegate delegateInstance = dynamicMethod.CreateDelegate(delegateType);
            MethodInfo mi = delegateInstance.Method;

            _cached[secret] = (mi, delegateInstance);
            return (mi, delegateInstance);
        }

        // +---------------+
        // | Ctors Hooking |
        // +---------------+
        public delegate ulong GenericCtorType(ulong self);

        private static MethodInfo UnifiedCtorMethodInfo = typeof(MsvcOffensiveGC).GetMethod(nameof(UnifiedCtor));

        public static ulong UnifiedCtor(string secret, ulong self)
        {
            RegisterClassName(self, secret);

            ulong res = 0x0bad_c0de_dead_c0de;
            int hashcode = 0x00000000;

            if (!_cached.ContainsKey(secret))
            {
                Console.WriteLine("[MsvcOffensiveGC] CAN'T FIND CTOR IN CACHE! Expect a crash! Offender in next line: ");
                Console.WriteLine(secret);
                return res;
            }
            var (originalHookMethodInfo, _) = _cached[secret];

            if (!DelegateStore.Real.ContainsKey(originalHookMethodInfo))
            {
                Console.WriteLine("[MsvcOffensiveGC] CAN'T FIND CTOR IN DelegateStore! Expect a crash! Offender in next line: ");
                Console.WriteLine(secret);
                return res;
            }
            var originalMethod = (GenericCtorType)DelegateStore.Real[originalHookMethodInfo];
            // Invoking original ctor
            res = originalMethod(self);
            return res;
        }

        // +--------------------------+
        // | __autoclassinit2 Hooking |
        // +--------------------------+
        public delegate void AutoClassInit2Type(ulong self, ulong size);

        private static MethodInfo UnifiedAutoClassInit2MethodInfo =
            typeof(MsvcOffensiveGC).GetMethod(nameof(UnifiedAutoClassInit2));

        public static void UnifiedAutoClassInit2(string secret, ulong self, ulong size)
        {
            // NOTE: Secret is not to be trusted here because __autoclassinit2 is often shared
            // between various classes in the same dll.
            RegisterSize(self, size);

            if (!_cached.ContainsKey(secret)) return;
            var (originalHookMethodInfo, _) = _cached[secret];

            if (!DelegateStore.Real.ContainsKey(originalHookMethodInfo)) return;
            var originalMethod = (AutoClassInit2Type)DelegateStore.Real[originalHookMethodInfo];
            // Invoking original ctor
            originalMethod(self, size);
        }


        // +----------------------+
        // | Operator new Hooking |
        // +----------------------+
        public delegate ulong OperatorNewType(ulong size);

        private static MethodInfo UnifiedOperatorNewMethodInfo =
            typeof(MsvcOffensiveGC).GetMethod(nameof(UnifiedOperatorNew));

        public static ulong UnifiedOperatorNew(string secret, ulong size)
        {
            ulong res = 0;

            if (!_cached.ContainsKey(secret))
                return res;
            var (originalHookMethodInfo, _) = _cached[secret];

            var originalMethod = (OperatorNewType)DelegateStore.Real[originalHookMethodInfo];
            // Invoking original ctor
            res = originalMethod(size);
            //Logger.Debug($"[OperatorNew] Invoked original. Size: {size}, returned addr: {res}");

            if (res != 0)
            {
                RegisterSize(res, size);
            }
            else
            {
                Logger.Debug("[Error] Operator new failed (returned null).");
            }

            return res;
        }


        // +---------------------------+
        // | Class Sizes Match  Making |
        // +---------------------------+
        private static LRUCache<ulong, ulong> _addrToSize = new LRUCache<ulong, ulong>(100);
        private static object _addrToSizeLock = new();
        private static Dictionary<string, ulong> ClassSizes = new Dictionary<string, ulong>();
        private static object ClassSizesLock = new();
        public IReadOnlyDictionary<string, ulong> GetFindings() => ClassSizes;

        public static void RegisterSize(ulong addr, ulong size)
        {
            lock (_addrToSizeLock)
            {
                _addrToSize.AddOrUpdate(addr, size);
            }
        }

        public static void RegisterClassName(ulong addr, string className)
        {
            // Check if we already found the size of this class
            lock (ClassSizesLock)
            {
                if (ClassSizes.ContainsKey(className))
                    return;
            }

            // Check recent "allocations"
            bool res;
            ulong size;
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
