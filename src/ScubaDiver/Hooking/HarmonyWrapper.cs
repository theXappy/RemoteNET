using System;
using System.Collections.Generic;
using HarmonyLib;
using System.Collections.Concurrent;
using System.Reflection;


namespace ScubaDiver.Hooking
{

    public class HarmonyWrapper
    {
        public static HarmonyWrapper Instance => new HarmonyWrapper();

        private HarmonyLib.Harmony _harmony;
        /// <summary>
        /// Maps 'target function parameters count' to right hook function (SinglePrefixHook_NUMBER)
        /// </summary>
        private Dictionary<int, MethodInfo> _psHooks;
        /// <summary>
        /// Maps methods and the prefix hooks that were used to hook them. (Important for unpatching)
        /// </summary>
        private Dictionary<string, MethodInfo> _singlePrefixHooks = new Dictionary<string, MethodInfo>();
        /// <summary>
        /// Thsis dict is static because <see cref="SinglePrefixHook"/> must be a static function (Harmony limitations)
        /// </summary>
        private static ConcurrentDictionary<string, HookCallback> _prefixHooks = new ConcurrentDictionary<string, HookCallback>();

        private HarmonyWrapper()
        {
            _harmony = new Harmony("xx.yy.zz");
            _psHooks = new Dictionary<int, MethodInfo>();
            for (int i = 0; i < int.MaxValue; i++)
            {
                MethodInfo spHook = AccessTools.Method(this.GetType(), $"SinglePrefixHook_{i}");
                if (spHook == null)
                {
                    // No more hooks available. We finished with (i-1) entries.
                    break;
                }
                _psHooks[i] = spHook;
            }
        }

        public delegate void HookCallback(object instance, object[] args);

        public void AddPrefix(MethodInfo target, HookCallback patch)
        {
            string uniqueId = target.DeclaringType.FullName + ":" + target.Name;
            _prefixHooks[uniqueId] = patch;

            int paramsCount = target.GetParameters().Length;
            MethodInfo spHook = _psHooks[paramsCount];
            // Document the `single prefix hook` used so we can remove later
            _singlePrefixHooks[uniqueId] = spHook;

            //_harmony.Patch(target, prefix: new HarmonyMethod(spHook));
            string prefixHookName = $"SinglePrefixHook_{paramsCount}";
            var prefix = this.GetType().GetMethod(prefixHookName, (BindingFlags)0xfffffff);

            _harmony.Patch(target, prefix: new HarmonyMethod(prefix));
        }

        public void RemovePrefix(MethodInfo target)
        {
            string uniqueId = target.DeclaringType.FullName + ":" + target.Name;
            if (_singlePrefixHooks.TryGetValue(uniqueId, out MethodInfo spHook))
            {
                _harmony.Unpatch(target, spHook);
            }
            _singlePrefixHooks.Remove(uniqueId);
            _prefixHooks.TryRemove(uniqueId, out _);
        }

        private static void SinglePrefixHook(MethodBase __originalMethod, object __instance, params object[] args)
        {
            string uniqueId = __originalMethod.DeclaringType.FullName + ":" + __originalMethod.Name;
            if (_prefixHooks.TryGetValue(uniqueId, out var funcHook))
            {
                funcHook(__instance, args);
            }
            else
            {
                Console.WriteLine("!ERROR! No such hooked func");
            }
        }

#pragma warning disable IDE0051 // Remove unused private members
        private static void SinglePrefixHook_0(MethodBase __originalMethod, object __instance) => SinglePrefixHook(__originalMethod, __instance);
        private static void SinglePrefixHook_1(MethodBase __originalMethod, object __instance, object __0) => SinglePrefixHook(__originalMethod, __instance, __0);
        private static void SinglePrefixHook_2(MethodBase __originalMethod, object __instance, object __0, object __1) => SinglePrefixHook(__originalMethod, __instance, __0, __1);
        private static void SinglePrefixHook_3(MethodBase __originalMethod, object __instance, object __0, object __1, object __2) => SinglePrefixHook(__originalMethod, __instance, __0, __1, __2);
        private static void SinglePrefixHook_4(MethodBase __originalMethod, object __instance, object __0, object __1, object __2, object __3) => SinglePrefixHook(__originalMethod, __instance, __0, __1, __2, __3);
        private static void SinglePrefixHook_5(MethodBase __originalMethod, object __instance, object __0, object __1, object __2, object __3, object __4) => SinglePrefixHook(__originalMethod, __instance, __0, __1, __2, __3, __4);
        private static void SinglePrefixHook_6(MethodBase __originalMethod, object __instance, object __0, object __1, object __2, object __3, object __4, object __5) => SinglePrefixHook(__originalMethod, __instance, __0, __1, __2, __3, __4, __5);
        private static void SinglePrefixHook_7(MethodBase __originalMethod, object __instance, object __0, object __1, object __2, object __3, object __4, object __5, object __6) => SinglePrefixHook(__originalMethod, __instance, __0, __1, __2, __3, __4, __5, __6);
        private static void SinglePrefixHook_8(MethodBase __originalMethod, object __instance, object __0, object __1, object __2, object __3, object __4, object __5, object __6, object __7) => SinglePrefixHook(__originalMethod, __instance, __0, __1, __2, __3, __4, __5, __6, __7);
        private static void SinglePrefixHook_9(MethodBase __originalMethod, object __instance, object __0, object __1, object __2, object __3, object __4, object __5, object __6, object __7, object __8) => SinglePrefixHook(__originalMethod, __instance, __0, __1, __2, __3, __4, __5, __6, __7, __8);
        private static void SinglePrefixHook_10(MethodBase __originalMethod, object __instance, object __0, object __1, object __2, object __3, object __4, object __5, object __6, object __7, object __8, object __9) => SinglePrefixHook(__originalMethod, __instance, __0, __1, __2, __3, __4, __5, __6, __7, __8, __9);
#pragma warning restore IDE0051 // Remove unused private members
    }
}


