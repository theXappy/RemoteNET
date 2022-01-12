using System;
using System.Collections.Generic;
using HarmonyLib;
using System.Collections.Concurrent;
using System.Reflection;
using ScubaDiver.API;

namespace ScubaDiver.Hooking
{

    public class HarmonyWrapper
    {
        public static HarmonyWrapper Instance => new HarmonyWrapper();

        private HarmonyLib.Harmony _harmony;
        /// <summary>
        /// Maps 'target function parameters count' to right hook function (UnifiedHook_NUMBER)
        /// </summary>
        private Dictionary<int, MethodInfo> _psHooks;
        /// <summary>
        /// Maps methods and the prefix hooks that were used to hook them. (Important for unpatching)
        /// </summary>
        private Dictionary<string, MethodInfo> _singlePrefixHooks = new Dictionary<string, MethodInfo>();
        /// <summary>
        /// Thsis dict is static because <see cref="SinglePrefixHook"/> must be a static function (Harmony limitations)
        /// </summary>
        private static ConcurrentDictionary<string, HookCallback> _actualHooks = new ConcurrentDictionary<string, HookCallback>();

        private HarmonyWrapper()
        {
            _harmony = new Harmony("xx.yy.zz");
            _psHooks = new Dictionary<int, MethodInfo>();
            for (int i = 0; i < int.MaxValue; i++)
            {
                MethodInfo spHook = AccessTools.Method(this.GetType(), $"UnifiedHook_{i}");
                if (spHook == null)
                {
                    // No more hooks available. We finished with (i-1) entries.
                    break;
                }
                _psHooks[i] = spHook;
            }
        }

        public delegate void HookCallback(object instance, object[] args);

        public void AddHook(MethodInfo target, HarmonyPatchPosition pos, HookCallback patch)
        {
            string uniqueId = target.DeclaringType.FullName + ":" + target.Name;
            _actualHooks[uniqueId] = patch;

            int paramsCount = target.GetParameters().Length;
            MethodInfo spHook = _psHooks[paramsCount];
            // Document the `single prefix hook` used so we can remove later
            _singlePrefixHooks[uniqueId] = spHook;

            string prefixHookName = $"UnifiedHook_{paramsCount}";
            var myPrefixHook = this.GetType().GetMethod(prefixHookName, (BindingFlags)0xfffffff);

            HarmonyMethod prefix = null;
            HarmonyMethod postfix = null;
            HarmonyMethod transpiler = null;
            HarmonyMethod finalizer = null;
            switch (pos)
            {
                case HarmonyPatchPosition.Prefix:
                    prefix = new HarmonyMethod(myPrefixHook);
                    break;
                case HarmonyPatchPosition.Postfix:
                    postfix = new HarmonyMethod(myPrefixHook);
                    break;
                case HarmonyPatchPosition.Finalizer:
                    finalizer = new HarmonyMethod(myPrefixHook);
                    break;
                default:
                    throw new ArgumentException("Invalid value for the `HarmonyPatchPosition pos` arg");
                    break;
            }
            _harmony.Patch(target,
                prefix,
                postfix,
                transpiler,
                finalizer);
        }

        public void RemovePrefix(MethodInfo target)
        {
            string uniqueId = target.DeclaringType.FullName + ":" + target.Name;
            if (_singlePrefixHooks.TryGetValue(uniqueId, out MethodInfo spHook))
            {
                _harmony.Unpatch(target, spHook);
            }
            _singlePrefixHooks.Remove(uniqueId);
            _actualHooks.TryRemove(uniqueId, out _);
        }

        private static void SinglePrefixHook(MethodBase __originalMethod, object __instance, params object[] args)
        {
            string uniqueId = __originalMethod.DeclaringType.FullName + ":" + __originalMethod.Name;
            if (_actualHooks.TryGetValue(uniqueId, out var funcHook))
            {
                funcHook(__instance, args);
            }
            else
            {
                Console.WriteLine("!ERROR! No such hooked func");
            }
        }

#pragma warning disable IDE0051 // Remove unused private members
        private static void UnifiedHook_0(MethodBase __originalMethod, object __instance) => SinglePrefixHook(__originalMethod, __instance);
        private static void UnifiedHook_1(MethodBase __originalMethod, object __instance, object __0) => SinglePrefixHook(__originalMethod, __instance, __0);
        private static void UnifiedHook_2(MethodBase __originalMethod, object __instance, object __0, object __1) => SinglePrefixHook(__originalMethod, __instance, __0, __1);
        private static void UnifiedHook_3(MethodBase __originalMethod, object __instance, object __0, object __1, object __2) => SinglePrefixHook(__originalMethod, __instance, __0, __1, __2);
        private static void UnifiedHook_4(MethodBase __originalMethod, object __instance, object __0, object __1, object __2, object __3) => SinglePrefixHook(__originalMethod, __instance, __0, __1, __2, __3);
        private static void UnifiedHook_5(MethodBase __originalMethod, object __instance, object __0, object __1, object __2, object __3, object __4) => SinglePrefixHook(__originalMethod, __instance, __0, __1, __2, __3, __4);
        private static void UnifiedHook_6(MethodBase __originalMethod, object __instance, object __0, object __1, object __2, object __3, object __4, object __5) => SinglePrefixHook(__originalMethod, __instance, __0, __1, __2, __3, __4, __5);
        private static void UnifiedHook_7(MethodBase __originalMethod, object __instance, object __0, object __1, object __2, object __3, object __4, object __5, object __6) => SinglePrefixHook(__originalMethod, __instance, __0, __1, __2, __3, __4, __5, __6);
        private static void UnifiedHook_8(MethodBase __originalMethod, object __instance, object __0, object __1, object __2, object __3, object __4, object __5, object __6, object __7) => SinglePrefixHook(__originalMethod, __instance, __0, __1, __2, __3, __4, __5, __6, __7);
        private static void UnifiedHook_9(MethodBase __originalMethod, object __instance, object __0, object __1, object __2, object __3, object __4, object __5, object __6, object __7, object __8) => SinglePrefixHook(__originalMethod, __instance, __0, __1, __2, __3, __4, __5, __6, __7, __8);
        private static void UnifiedHook_10(MethodBase __originalMethod, object __instance, object __0, object __1, object __2, object __3, object __4, object __5, object __6, object __7, object __8, object __9) => SinglePrefixHook(__originalMethod, __instance, __0, __1, __2, __3, __4, __5, __6, __7, __8, __9);
#pragma warning restore IDE0051 // Remove unused private members
    }
}


