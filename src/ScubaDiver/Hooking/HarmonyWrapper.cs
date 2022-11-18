using System;
using System.Collections.Generic;
using HarmonyLib;
using System.Collections.Concurrent;
using System.Reflection;
using ScubaDiver.API.Hooking;

namespace ScubaDiver.Hooking
{
    public class HarmonyWrapper
    {
        private static HarmonyWrapper _instance = null;
        public static HarmonyWrapper Instance => _instance ??= new();

        private readonly Harmony _harmony;
        /// <summary>
        /// Maps 'target function parameters count' to right hook function (UnifiedHook_NUMBER)
        /// </summary>
        private readonly Dictionary<int, MethodInfo> _psHooks;
        /// <summary>
        /// Maps methods and the prefix hooks that were used to hook them. (Important for unpatching)
        /// </summary>
        private readonly Dictionary<string, MethodInfo> _singlePrefixHooks = new();

        /// <summary>
        /// Used by <see cref="SinglePrefixHook"/> to guarantee hooking code doesn't cause infinite recursion
        /// </summary>
        private static readonly SmartLocksDict<MethodBase> _locksDict = new();


        /// <summary>
        /// Thsis dict is static because <see cref="SinglePrefixHook"/> must be a static function (Harmony limitations)
        /// </summary>
        private static readonly ConcurrentDictionary<string, HookCallback> _actualHooks = new();

        private HarmonyWrapper()
        {
            _harmony = new Harmony("xx.yy.zz");
            _psHooks = new Dictionary<int, MethodInfo>();
            for (int i = 0; i < int.MaxValue; i++)
            {
                MethodInfo spHook = AccessTools.Method(this.GetType(), $"UnifiedHook_{i}");
                if (spHook == null)
                {
                    // No more hooks available. We finished with i entries, the last valid entry had (i-1) parameters.
                    break;
                }
                _psHooks[i] = spHook;
            }
        }

        /// <summary>
        /// A "Framework Thread" is a thread currently used to invoke ScubaDiver framework code.
        /// It's important for us to mark those threads because if they, by accident, reach a method that was hooked
        /// we DO NOT want the hook to trigger.
        /// We only want the hooks to trigger on "normal method invocations" within the target's code.
        /// Note that there's an exception to that rule: If a thread is assigned to run SOME ScubaDiver framework code which
        /// eventually drift into "normal" code (2 examples: Invocation of a remote object's method & calling of a remote constructor)
        /// then we DO want hooks to run (the user might be explicitly calling a function so it triggers some other function & it's hook 
        /// to check they got it right or for other reasons).
        /// </summary>
        public void RegisterFrameworkThread(int id)
        {
            _locksDict.SetSpecialThreadState(id, SmartLocksDict<MethodBase>.SmartLockThreadState.ForbidLocking);
        }
        public void AllowFrameworkThreadToTrigger(int id)
        {
            _locksDict.SetSpecialThreadState(id, SmartLocksDict<MethodBase>.SmartLockThreadState.ForbidLocking | SmartLocksDict<MethodBase>.SmartLockThreadState.TemporarilyAllowLocks);
        }
        public void DisallowFrameworkThreadToTrigger(int id)
        {
            _locksDict.SetSpecialThreadState(id, SmartLocksDict<MethodBase>.SmartLockThreadState.ForbidLocking);
        }
        public void UnregisterFrameworkThread(int id)
        {
            _locksDict.SetSpecialThreadState(id, SmartLocksDict<MethodBase>.SmartLockThreadState.AllowAllLocks);
        }

        public delegate void HookCallback(object instance, object[] args);

        public void AddHook(MethodBase target, HarmonyPatchPosition pos, HookCallback patch)
        {
            //
            // Save a side the patch callback to invoke when the target is called
            //
            string uniqueId = target.DeclaringType.FullName + ":" + target.Name;
            _actualHooks[uniqueId] = patch;

            //
            // Actual hook for the method is the generic "SinglePrefixHook" (through one if its proxies 'UnifiedHook_X')
            // the "SinglePrefixHook" will search for the above saved callback and invoke it itself.
            //
            int paramsCount = target.GetParameters().Length;
            MethodInfo myPrefixHook = _psHooks[paramsCount];
            // Document the `single prefix hook` used so we can remove later
            _singlePrefixHooks[uniqueId] = myPrefixHook;
            _locksDict.Add(target);

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
            }
            _harmony.Patch(target,
                prefix,
                postfix,
                transpiler,
                finalizer);
        }

        public void RemovePrefix(MethodBase target)
        {
            string uniqueId = target.DeclaringType.FullName + ":" + target.Name;
            if (_singlePrefixHooks.TryGetValue(uniqueId, out MethodInfo spHook))
            {
                _harmony.Unpatch(target, spHook);
            }
            _singlePrefixHooks.Remove(uniqueId);
            _actualHooks.TryRemove(uniqueId, out _);
            _locksDict.Remove(target);
        }

        

        private static void SinglePrefixHook(MethodBase __originalMethod, object __instance, params object[] args)
        {
            SmartLocksDict<MethodBase>.AcquireResults res = _locksDict.Acquire(__originalMethod);
            if(res == SmartLocksDict<MethodBase>.AcquireResults.AlreadyAcquireByCurrentThread ||
                res == SmartLocksDict<MethodBase>.AcquireResults.ThreadNotAllowedToLock
                )
            {
                // Whoops looks like we patched a method used in the 'ScubaDvier framework code'
                // Luckily, this if clause allows us to avoid recursion
                return;
            }

            try
            {
                string uniqueId = __originalMethod.DeclaringType.FullName + ":" + __originalMethod.Name;
                if (_actualHooks.TryGetValue(uniqueId, out HookCallback funcHook))
                {
                    funcHook(__instance, args);
                }
                else
                {
                    Console.WriteLine("!ERROR! No such hooked func");
                }
            }
            finally
            {
                _locksDict.Release(__originalMethod);
            }
        }

#pragma warning disable IDE0051 // Remove unused private members
        // ReSharper disable UnusedMember.Local
        private static void UnifiedHook_0(MethodBase __originalMethod, object __instance) => SinglePrefixHook(__originalMethod, __instance);
        private static void UnifiedHook_1(MethodBase __originalMethod, object __instance, ref object __0) => SinglePrefixHook(__originalMethod, __instance, __0);
        private static void UnifiedHook_2(MethodBase __originalMethod, object __instance, ref object __0, ref object __1) => SinglePrefixHook(__originalMethod, __instance, __0, __1);
        private static void UnifiedHook_3(MethodBase __originalMethod, object __instance, ref object __0, ref object __1, ref object __2) => SinglePrefixHook(__originalMethod, __instance, __0, __1, __2);
        private static void UnifiedHook_4(MethodBase __originalMethod, object __instance, ref object __0, ref object __1, ref object __2, ref object __3) => SinglePrefixHook(__originalMethod, __instance, __0, __1, __2, __3);
        private static void UnifiedHook_5(MethodBase __originalMethod, object __instance, ref object __0, ref object __1, ref object __2, ref object __3, ref object __4) => SinglePrefixHook(__originalMethod, __instance, __0, __1, __2, __3, __4);
        private static void UnifiedHook_6(MethodBase __originalMethod, object __instance, ref object __0, ref object __1, ref object __2, ref object __3, ref object __4, ref object __5) => SinglePrefixHook(__originalMethod, __instance, __0, __1, __2, __3, __4, __5);
        private static void UnifiedHook_7(MethodBase __originalMethod, object __instance, ref object __0, ref object __1, ref object __2, ref object __3, ref object __4, ref object __5, ref object __6) => SinglePrefixHook(__originalMethod, __instance, __0, __1, __2, __3, __4, __5, __6);
        private static void UnifiedHook_8(MethodBase __originalMethod, object __instance, ref object __0, ref object __1, ref object __2, ref object __3, ref object __4, ref object __5, ref object __6, ref object __7) => SinglePrefixHook(__originalMethod, __instance, __0, __1, __2, __3, __4, __5, __6, __7);
        private static void UnifiedHook_9(MethodBase __originalMethod, object __instance, ref object __0, ref object __1, ref object __2, ref object __3, ref object __4, ref object __5, ref object __6, ref object __7, ref object __8) => SinglePrefixHook(__originalMethod, __instance, __0, __1, __2, __3, __4, __5, __6, __7, __8);
        private static void UnifiedHook_10(MethodBase __originalMethod, object __instance, ref object __0, ref object __1, ref object __2, ref object __3, ref object __4, ref object __5, ref object __6, ref object __7, ref object __8, ref object __9) => SinglePrefixHook(__originalMethod, __instance, __0, __1, __2, __3, __4, __5, __6, __7, __8, __9);
        // ReSharper restore UnusedMember.Local
#pragma warning restore IDE0051 // Remove unused private members
    }
}


