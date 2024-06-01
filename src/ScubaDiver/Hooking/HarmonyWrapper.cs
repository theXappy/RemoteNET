using System;
using System.Collections.Generic;
using HarmonyLib;
using System.Collections.Concurrent;
using System.Linq;
using System.Reflection;
using ScubaDiver.API.Hooking;

namespace ScubaDiver.Hooking
{
    public class HarmonyWrapper
    {
        // We'll use this class to indicate a some parameter in a hooked function can't be proxied
        public class DummyParameterReplacement
        {
            public static readonly DummyParameterReplacement Instance = new();

            public override string ToString()
            {
                return
                    "Dummy Parameter. The real parameter couldn't be proxied (probably because it's a ref struct) so you got this instead.";
            }
        }
        public class DummyThisReplacement
        {
            public static readonly DummyThisReplacement Instance = new();

            public override string ToString()
            {
                return
                    "Dummy Object Instance. The real parameter couldn't be proxied (probably because you hooked a ctor)";
            }
        }

        private static HarmonyWrapper _instance = null;
        public static HarmonyWrapper Instance => _instance ??= new();

        private readonly Harmony _harmony;

        /// <summary>
        /// Maps 'target function parameters bitmap' to PREFIX unified hook function (UnifiedHook_Prefix_NUMBER)
        /// </summary>
        private readonly Dictionary<string, MethodInfo> _unifiedPrefixHooks;

        /// <summary>
        /// Maps 'target function parameters bitmap' to POSTFIX unified hook function (UnifiedHook_Postfix_NUMBER)
        /// </summary>
        private readonly Dictionary<string, MethodInfo> _unifiedPostfixHooks;

        /// <summary>
        /// Maps methods and the prefix hooks that were used to hook them. (Important for unpatching)
        /// </summary>
        private readonly Dictionary<string, MethodInfo> _singlePrefixHooks = new();

        /// <summary>
        /// Used by <see cref="SingleHook"/> to guarantee hooking code doesn't cause infinite recursion
        /// </summary>
        private static readonly SmartLocksDict<MethodBase> _locksDict = new();


        /// <summary>
        /// Thsis dict is static because <see cref="SingleHook"/> must be a static function (Harmony limitations)
        /// </summary>
        private static readonly ConcurrentDictionary<string, HookCallback> _actualHooks = new();

        private HarmonyWrapper()
        {
            _harmony = new Harmony("xx.yy.zz");
            _unifiedPrefixHooks = new Dictionary<string, MethodInfo>();
            _unifiedPostfixHooks = new Dictionary<string, MethodInfo>();
            var methods = typeof(HarmonyWrapper).GetMethods((BindingFlags)0xffffff);
            foreach (MethodInfo method in methods)
            {
                // Collect prefix Unified Hooks
                if (method.Name.StartsWith("UnifiedHook_Prefix_"))
                {
                    string argsBitmap = method.Name.Substring("UnifiedHook_Prefix_".Length);
                    _unifiedPrefixHooks[argsBitmap] = method;
                }

                // Collect postfix Unified Hooks
                if (method.Name.StartsWith("UnifiedHook_Postfix_"))
                {
                    string argsBitmap = method.Name.Substring("UnifiedHook_Postfix_".Length);
                    _unifiedPostfixHooks[argsBitmap] = method;
                }
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
            _locksDict.SetSpecialThreadState(id,
                SmartLocksDict<MethodBase>.SmartLockThreadState.ForbidLocking |
                SmartLocksDict<MethodBase>.SmartLockThreadState.TemporarilyAllowLocks);
        }

        public void DisallowFrameworkThreadToTrigger(int id)
        {
            _locksDict.SetSpecialThreadState(id, SmartLocksDict<MethodBase>.SmartLockThreadState.ForbidLocking);
        }

        public void UnregisterFrameworkThread(int id)
        {
            _locksDict.SetSpecialThreadState(id, SmartLocksDict<MethodBase>.SmartLockThreadState.AllowAllLocks);
        }


        /// <returns>Skip original</returns>
        public delegate bool HookCallback(object instance, object[] args, ref object retValue);

        public void AddHook(MethodBase target, HarmonyPatchPosition pos, HookCallback patch)
        {
            //
            // Save a side the patch callback to invoke when the target is called
            //
            string argsStr = string.Join(";", target.GetParameters().Select(pi => pi.ParameterType.FullName));
            string uniqueId = target.DeclaringType.FullName + ":" + argsStr + ":"+ target.Name + ":" + pos;
            if (_actualHooks.ContainsKey(uniqueId))
            {
                Logger.Debug($"Hook already exists under (not so) unique ID: {uniqueId}");
                throw new ArgumentException($"Hooke already exists under (not so) unique ID: {uniqueId}");
            }

            _actualHooks[uniqueId] = patch;

            //
            // Actual hook for the method is the generic "SinglePrefixHook" (through one if its proxies 'UnifiedHook_X')
            // the "SinglePrefixHook" will search for the above saved callback and invoke it itself.
            //


            MethodInfo myHook;
            if (target.IsConstructor)
            {
                myHook = typeof(HarmonyWrapper).GetMethod("UnifiedHook_ctor", (BindingFlags)0xffff);
            }
            else
            {
                myHook = GetUnifiedHook(target as MethodInfo, pos);
            }

            Logger.Debug($"[HarmonyWrapper] Choose this hook myHook: " + myHook);
            Logger.Debug($"[HarmonyWrapper] Hooking this position: " + pos);



            // Document the `single prefix hook` used so we can remove later
            _singlePrefixHooks[uniqueId] = myHook;
            _locksDict.Add(target);

            HarmonyMethod prefix = null;
            HarmonyMethod postfix = null;
            HarmonyMethod transpiler = null;
            HarmonyMethod finalizer = null;
            switch (pos)
            {
                case HarmonyPatchPosition.Prefix:
                    prefix = new HarmonyMethod(myHook);
                    break;
                case HarmonyPatchPosition.Postfix:
                    postfix = new HarmonyMethod(myHook);
                    break;
                case HarmonyPatchPosition.Finalizer:
                    finalizer = new HarmonyMethod(myHook);
                    break;
                default:
                    throw new ArgumentException("Invalid value for the 'HarmonyPatchPosition pos' arg");
            }

            _harmony.Patch(target,
                prefix,
                postfix,
                transpiler,
                finalizer);

        }

        private MethodInfo GetUnifiedHook(MethodInfo target, HarmonyPatchPosition pos)
        {
            var parameters = target.GetParameters();
            int paramsCount = parameters.Length;
            int[] hookableParametersFlags = new int[10];
            for (int i = 0; i < paramsCount; i++)
            {
                ParameterInfo parameter = parameters[i];
                bool isRefStruct = IsRefStruct(parameter.ParameterType);
                hookableParametersFlags[i] = isRefStruct ? 0 : 1;
            }

            // Now we need to turn the pararmeters flags into a "binary" string.
            // For example:
            // {0, 0, 1, 0} ---> "0010"
            string functionSignatureSummary =
                string.Join(string.Empty, hookableParametersFlags.Select(i => i.ToString()));

            if (target.ReturnType == typeof(void))
            {
                functionSignatureSummary += "_NoReturn";
            }

            Logger.Debug(
                $"[HarmonyWrapper][AddHook] Constructed this binaryParamsString: {functionSignatureSummary} for method {target.Name}");

            switch (pos)
            {
                case HarmonyPatchPosition.Prefix:
                    return _unifiedPrefixHooks[functionSignatureSummary];
                case HarmonyPatchPosition.Postfix:
                    return _unifiedPostfixHooks[functionSignatureSummary];
                case HarmonyPatchPosition.Finalizer:
                default:
                    throw new ArgumentOutOfRangeException(nameof(pos), pos, null);
            }

            bool IsRefStruct(Type t)
            {
                var isByRefLikeProp = typeof(Type).GetProperties().FirstOrDefault(p => p.Name == "IsByRefLike");
                bool isDotNetCore = isByRefLikeProp != null;
                if (!isDotNetCore)
                    return false;

                bool isByRef = t.IsByRef;
                bool isByRefLike = (bool)isByRefLikeProp.GetValue(t);
                if (isByRefLike)
                    return true;

                if (!isByRef)
                    return false;

                // Otherwise, we have a "ref" type. This could be things like:
                // Object&
                // Span<byte>&
                // We must look into the inner type to figure out if it's
                // RefLike (e.g., Span<byte>) or not (e.g., Object)
                return IsRefStruct(t.GetElementType());
            }
        }

        public void UnhookAnyHookPosition(MethodBase target)
        {
            foreach (HarmonyPatchPosition pos in new[] { HarmonyPatchPosition.Prefix, HarmonyPatchPosition.Postfix })
            {
                string argsStr = string.Join(";", target.GetParameters().Select(pi => pi.ParameterType.FullName));
                string uniqueId = target.DeclaringType.FullName + ":" + argsStr + ":" + target.Name + ":" + pos;
                if (_singlePrefixHooks.TryGetValue(uniqueId, out MethodInfo spHook))
                {
                    _harmony.Unpatch(target, spHook);
                }

                _singlePrefixHooks.Remove(uniqueId);
                _actualHooks.TryRemove(uniqueId, out _);
                _locksDict.Remove(target);
            }
        }

        private static bool SinglePrefixHookNoReturn(MethodBase __originalMethod, object __instance,
            params object[] args)
        {
            object __result = null;
            return SinglePrefixHook(__originalMethod, ref __result, __instance, args);
        }

        private static bool SinglePrefixHook(MethodBase __originalMethod, ref object __result, object __instance,
            params object[] args)
        {
            return SingleHook(__originalMethod, HarmonyPatchPosition.Prefix, ref __result, __instance, args);
        }

        private static void SinglePostfixHookNoReturn(MethodBase __originalMethod, object __instance,
            params object[] args)
        {
            object __result = null;
            SinglePostfixHook(__originalMethod, ref __result, __instance, args);
        }
        private static void SinglePostfixHook(MethodBase __originalMethod, ref object __result, object __instance,
            params object[] args)
        {
            SingleHook(__originalMethod, HarmonyPatchPosition.Postfix, ref __result, __instance, args);
        }

        private static bool SingleHook(MethodBase __originalMethod, HarmonyPatchPosition pos, ref object __result, object __instance, params object[] args)
        {
            SmartLocksDict<MethodBase>.AcquireResults res = _locksDict.Acquire(__originalMethod);
            if (res == SmartLocksDict<MethodBase>.AcquireResults.AlreadyAcquireByCurrentThread ||
                res == SmartLocksDict<MethodBase>.AcquireResults.ThreadNotAllowedToLock
                )
            {
                // Whoops looks like we patched a method used in the 'ScubaDvier framework code'
                // Luckily, this if clause allows us to avoid recursion

                return false; // Don't skip original
            }

            try
            {
                string argsStr = string.Join(";", __originalMethod.GetParameters().Select(pi => pi.ParameterType.FullName));
                string uniqueId = __originalMethod.DeclaringType.FullName + ":" + argsStr + ":" + __originalMethod.Name + ":" + pos;
                if (_actualHooks.TryGetValue(uniqueId, out HookCallback funcHook))
                {
                    // Return value will determine wether the original method will be called or not.
                    return funcHook(__instance, args, ref __result);
                }
                else
                {
                    Console.WriteLine("!ERROR! No such hooked func");
                    return false; // Don't skip original
                }
            }
            finally
            {
                _locksDict.Release(__originalMethod);
            }
        }

#pragma warning disable IDE0051 // Remove unused private members
        // ReSharper disable UnusedMember.Local
        // ReSharper disable InconsistentNaming
        private static void UnifiedHook_ctor(MethodBase __originalMethod) => SinglePrefixHookNoReturn(__originalMethod, DummyParameterReplacement.Instance);
        private static bool UnifiedHook_Prefix_0000000000(MethodBase __originalMethod, object __instance, ref object __result) => SinglePrefixHook(__originalMethod, ref __result, __instance);
        private static bool UnifiedHook_Prefix_0000000000_NoReturn(MethodBase __originalMethod, object __instance) => SinglePrefixHookNoReturn(__originalMethod, __instance);
        private static bool UnifiedHook_Prefix_1000000000(MethodBase __originalMethod, object __instance, object __0, ref object __result) => SinglePrefixHook(__originalMethod, ref __result, __instance, __0);
        private static bool UnifiedHook_Prefix_1000000000_NoReturn(MethodBase __originalMethod, object __instance, object __0) => SinglePrefixHookNoReturn(__originalMethod, __instance, __0);
        private static bool UnifiedHook_Prefix_1100000000(MethodBase __originalMethod, object __instance, object __0, object __1, ref object __result) => SinglePrefixHook(__originalMethod, ref __result, __instance, __0, __1);
        private static bool UnifiedHook_Prefix_1100000000_NoReturn(MethodBase __originalMethod, object __instance, object __0, object __1) => SinglePrefixHookNoReturn(__originalMethod, __instance, __0, __1);
        private static bool UnifiedHook_Prefix_0100000000(MethodBase __originalMethod, object __instance, object __1, ref object __result) => SinglePrefixHook(__originalMethod, ref __result, __instance, DummyParameterReplacement.Instance, __1);
        private static bool UnifiedHook_Prefix_0100000000_NoReturn(MethodBase __originalMethod, object __instance, object __1) => SinglePrefixHookNoReturn(__originalMethod, __instance, DummyParameterReplacement.Instance, __1);
        private static bool UnifiedHook_Prefix_1110000000(MethodBase __originalMethod, object __instance, object __0, object __1, object __2, ref object __result) => SinglePrefixHook(__originalMethod, ref __result, __instance, __0, __1, __2);
        private static bool UnifiedHook_Prefix_1110000000_NoReturn(MethodBase __originalMethod, object __instance, object __0, object __1, object __2) => SinglePrefixHookNoReturn(__originalMethod, __instance, __0, __1, __2);
        private static bool UnifiedHook_Prefix_0110000000(MethodBase __originalMethod, object __instance, object __1, object __2, ref object __result) => SinglePrefixHook(__originalMethod, ref __result, __instance, DummyParameterReplacement.Instance, __1, __2);
        private static bool UnifiedHook_Prefix_0110000000_NoReturn(MethodBase __originalMethod, object __instance, object __1, object __2) => SinglePrefixHookNoReturn(__originalMethod, __instance, DummyParameterReplacement.Instance, __1, __2);
        private static bool UnifiedHook_Prefix_1010000000(MethodBase __originalMethod, object __instance, object __0, object __2, ref object __result) => SinglePrefixHook(__originalMethod, ref __result, __instance, __0, DummyParameterReplacement.Instance, __2);
        private static bool UnifiedHook_Prefix_1010000000_NoReturn(MethodBase __originalMethod, object __instance, object __0, object __2) => SinglePrefixHookNoReturn(__originalMethod, __instance, __0, DummyParameterReplacement.Instance, __2);
        private static bool UnifiedHook_Prefix_0010000000(MethodBase __originalMethod, object __instance, object __2, ref object __result) => SinglePrefixHook(__originalMethod, ref __result, __instance, DummyParameterReplacement.Instance, DummyParameterReplacement.Instance, __2);
        private static bool UnifiedHook_Prefix_0010000000_NoReturn(MethodBase __originalMethod, object __instance, object __2) => SinglePrefixHookNoReturn(__originalMethod, __instance, DummyParameterReplacement.Instance, DummyParameterReplacement.Instance, __2);
        private static bool UnifiedHook_Prefix_1111000000(MethodBase __originalMethod, object __instance, object __0, object __1, object __2, object __3, ref object __result) => SinglePrefixHook(__originalMethod, ref __result, __instance, __0, __1, __2, __3);
        private static bool UnifiedHook_Prefix_1111000000_NoReturn(MethodBase __originalMethod, object __instance, object __0, object __1, object __2, object __3) => SinglePrefixHookNoReturn(__originalMethod, __instance, __0, __1, __2, __3);
        private static bool UnifiedHook_Prefix_1111100000(MethodBase __originalMethod, object __instance, object __0, object __1, object __2, object __3, object __4, ref object __result) => SinglePrefixHook(__originalMethod, ref __result, __instance, __0, __1, __2, __3, __4);
        private static bool UnifiedHook_Prefix_1111100000_NoReturn(MethodBase __originalMethod, object __instance, object __0, object __1, object __2, object __3, object __4) => SinglePrefixHookNoReturn(__originalMethod, __instance, __0, __1, __2, __3, __4);
        private static bool UnifiedHook_Prefix_1111110000(MethodBase __originalMethod, object __instance, object __0, object __1, object __2, object __3, object __4, object __5, ref object __result) => SinglePrefixHook(__originalMethod, ref __result, __instance, __0, __1, __2, __3, __4, __5);
        private static bool UnifiedHook_Prefix_1111110000_NoReturn(MethodBase __originalMethod, object __instance, object __0, object __1, object __2, object __3, object __4, object __5) => SinglePrefixHookNoReturn(__originalMethod, __instance, __0, __1, __2, __3, __4, __5);
        private static bool UnifiedHook_Prefix_1111111000(MethodBase __originalMethod, object __instance, object __0, object __1, object __2, object __3, object __4, object __5, object __6, ref object __result) => SinglePrefixHook(__originalMethod, ref __result, __instance, __0, __1, __2, __3, __4, __5, __6);
        private static bool UnifiedHook_Prefix_1111111000_NoReturn(MethodBase __originalMethod, object __instance, object __0, object __1, object __2, object __3, object __4, object __5, object __6) => SinglePrefixHookNoReturn(__originalMethod, __instance, __0, __1, __2, __3, __4, __5, __6);
        private static bool UnifiedHook_Prefix_1111111100(MethodBase __originalMethod, object __instance, object __0, object __1, object __2, object __3, object __4, object __5, object __6, object __7, ref object __result) => SinglePrefixHook(__originalMethod, ref __result, __instance, __0, __1, __2, __3, __4, __5, __6, __7);
        private static bool UnifiedHook_Prefix_1111111110(MethodBase __originalMethod, object __instance, object __0, object __1, object __2, object __3, object __4, object __5, object __6, object __7, object __8, ref object __result) => SinglePrefixHook(__originalMethod, ref __result, __instance, __0, __1, __2, __3, __4, __5, __6, __7, __8);
        private static bool UnifiedHook_Prefix_1111111110_NoReturn(MethodBase __originalMethod, object __instance, object __0, object __1, object __2, object __3, object __4, object __5, object __6, object __7, object __8) => SinglePrefixHookNoReturn(__originalMethod, __instance, __0, __1, __2, __3, __4, __5, __6, __7, __8);
        private static bool UnifiedHook_Prefix_1111111111(MethodBase __originalMethod, object __instance, object __0, object __1, object __2, object __3, object __4, object __5, object __6, object __7, object __8, object __9, ref object __result) => SinglePrefixHook(__originalMethod, ref __result, __instance, __0, __1, __2, __3, __4, __5, __6, __7, __8, __9);
        private static bool UnifiedHook_Prefix_1111111111_NoReturn(MethodBase __originalMethod, object __instance, object __0, object __1, object __2, object __3, object __4, object __5, object __6, object __7, object __8, object __9) => SinglePrefixHookNoReturn(__originalMethod, __instance, __0, __1, __2, __3, __4, __5, __6, __7, __8, __9);
        private static void UnifiedHook_Postfix_0000000000(MethodBase __originalMethod, object __instance, ref object __result) => SinglePostfixHook(__originalMethod, ref __result, __instance);
        private static void UnifiedHook_Postfix_0000000000_NoReturn(MethodBase __originalMethod, object __instance) => SinglePostfixHookNoReturn(__originalMethod, __instance);
        private static void UnifiedHook_Postfix_1000000000(MethodBase __originalMethod, object __instance, object __0, ref object __result) => SinglePostfixHook(__originalMethod, ref __result, __instance, __0);
        private static void UnifiedHook_Postfix_1000000000_NoReturn(MethodBase __originalMethod, object __instance, object __0) => SinglePostfixHookNoReturn(__originalMethod, __instance, __0);
        private static void UnifiedHook_Postfix_1100000000(MethodBase __originalMethod, object __instance, object __0, object __1, ref object __result) => SinglePostfixHook(__originalMethod, ref __result, __instance, __0, __1);
        private static void UnifiedHook_Postfix_1100000000_NoReturn(MethodBase __originalMethod, object __instance, object __0, object __1) => SinglePostfixHookNoReturn(__originalMethod, __instance, __0, __1);
        private static void UnifiedHook_Postfix_0100000000(MethodBase __originalMethod, object __instance, object __1, ref object __result) => SinglePostfixHook(__originalMethod, ref __result, __instance, __1);
        private static void UnifiedHook_Postfix_0100000000_NoReturn(MethodBase __originalMethod, object __instance, object __1) => SinglePostfixHookNoReturn(__originalMethod, __instance, __1);
        private static void UnifiedHook_Postfix_1110000000(MethodBase __originalMethod, object __instance, object __0, object __1, object __2, ref object __result) => SinglePostfixHook(__originalMethod, ref __result, __instance, __0, __1, __2);
        private static void UnifiedHook_Postfix_1110000000_NoReturn(MethodBase __originalMethod, object __instance, object __0, object __1, object __2) => SinglePostfixHookNoReturn(__originalMethod, __instance, __0, __1, __2);
        private static void UnifiedHook_Postfix_0110000000(MethodBase __originalMethod, object __instance, object __1, object __2, ref object __result) => SinglePostfixHook(__originalMethod, ref __result, __instance, __1, __2);
        private static void UnifiedHook_Postfix_0110000000_NoReturn(MethodBase __originalMethod, object __instance, object __1, object __2) => SinglePostfixHookNoReturn(__originalMethod, __instance, __1, __2);
        private static void UnifiedHook_Postfix_1010000000(MethodBase __originalMethod, object __instance, object __0, object __2, ref object __result) => SinglePostfixHook(__originalMethod, ref __result, __instance, __0, __2);
        private static void UnifiedHook_Postfix_1010000000_NoReturn(MethodBase __originalMethod, object __instance, object __0, object __2) => SinglePostfixHookNoReturn(__originalMethod, __instance, __0, __2);
        private static void UnifiedHook_Postfix_0010000000(MethodBase __originalMethod, object __instance, object __2, ref object __result) => SinglePostfixHook(__originalMethod, ref __result, __instance, __2);
        private static void UnifiedHook_Postfix_0010000000_NoReturn(MethodBase __originalMethod, object __instance, object __2) => SinglePostfixHookNoReturn(__originalMethod, __instance, __2);
        private static void UnifiedHook_Postfix_1111000000(MethodBase __originalMethod, object __instance, object __0, object __1, object __2, object __3, ref object __result) => SinglePostfixHook(__originalMethod, ref __result, __instance, __0, __1, __2, __3);
        private static void UnifiedHook_Postfix_1111000000_NoReturn(MethodBase __originalMethod, object __instance, object __0, object __1, object __2, object __3) => SinglePostfixHookNoReturn(__originalMethod, __instance, __0, __1, __2, __3);
        private static void UnifiedHook_Postfix_1111100000(MethodBase __originalMethod, object __instance, object __0, object __1, object __2, object __3, object __4, ref object __result) => SinglePostfixHook(__originalMethod, ref __result, __instance, __0, __1, __2, __3, __4);
        private static void UnifiedHook_Postfix_1111100000_NoReturn(MethodBase __originalMethod, object __instance, object __0, object __1, object __2, object __3, object __4) => SinglePostfixHookNoReturn(__originalMethod, __instance, __0, __1, __2, __3, __4);
        private static void UnifiedHook_Postfix_1111110000(MethodBase __originalMethod, object __instance, object __0, object __1, object __2, object __3, object __4, object __5, ref object __result) => SinglePostfixHook(__originalMethod, ref __result, __instance, __0, __1, __2, __3, __4, __5);
        private static void UnifiedHook_Postfix_1111110000_NoReturn(MethodBase __originalMethod, object __instance, object __0, object __1, object __2, object __3, object __4, object __5) => SinglePostfixHookNoReturn(__originalMethod, __instance, __0, __1, __2, __3, __4, __5);
        private static void UnifiedHook_Postfix_1111111000(MethodBase __originalMethod, object __instance, object __0, object __1, object __2, object __3, object __4, object __5, object __6, ref object __result) => SinglePostfixHook(__originalMethod, ref __result, __instance, __0, __1, __2, __3, __4, __5, __6);
        private static void UnifiedHook_Postfix_1111111000_NoReturn(MethodBase __originalMethod, object __instance, object __0, object __1, object __2, object __3, object __4, object __5, object __6) => SinglePostfixHookNoReturn(__originalMethod, __instance, __0, __1, __2, __3, __4, __5, __6);
        private static void UnifiedHook_Postfix_1111111100(MethodBase __originalMethod, object __instance, object __0, object __1, object __2, object __3, object __4, object __5, object __6, object __7, ref object __result) => SinglePostfixHook(__originalMethod, ref __result, __instance, __0, __1, __2, __3, __4, __5, __6, __7);
        private static void UnifiedHook_Postfix_1111111100_NoReturn(MethodBase __originalMethod, object __instance, object __0, object __1, object __2, object __3, object __4, object __5, object __6, object __7) => SinglePostfixHookNoReturn(__originalMethod, __instance, __0, __1, __2, __3, __4, __5, __6, __7);
        private static void UnifiedHook_Postfix_1111111110(MethodBase __originalMethod, object __instance, object __0, object __1, object __2, object __3, object __4, object __5, object __6, object __7, object __8, ref object __result) => SinglePostfixHook(__originalMethod, ref __result, __instance, __0, __1, __2, __3, __4, __5, __6, __7, __8);
        private static void UnifiedHook_Postfix_1111111110_NoReturn(MethodBase __originalMethod, object __instance, object __0, object __1, object __2, object __3, object __4, object __5, object __6, object __7, object __8) => SinglePostfixHookNoReturn(__originalMethod, __instance, __0, __1, __2, __3, __4, __5, __6, __7, __8);
        private static void UnifiedHook_Postfix_1111111111(MethodBase __originalMethod, object __instance, object __0, object __1, object __2, object __3, object __4, object __5, object __6, object __7, object __8, object __9, ref object __result) => SinglePostfixHook(__originalMethod, ref __result, __instance, __0, __1, __2, __3, __4, __5, __6, __7, __8, __9);
        private static void UnifiedHook_Postfix_1111111111_NoReturn(MethodBase __originalMethod, object __instance, object __0, object __1, object __2, object __3, object __4, object __5, object __6, object __7, object __8, object __9) => SinglePostfixHookNoReturn(__originalMethod, __instance, __0, __1, __2, __3, __4, __5, __6, __7, __8, __9);



        // ReSharper restore InconsistentNaming
        // ReSharper restore UnusedMember.Local
#pragma warning restore IDE0051 // Remove unused private members
    }
}


