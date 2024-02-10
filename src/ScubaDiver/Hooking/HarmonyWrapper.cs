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

        private static HarmonyWrapper _instance = null;
        public static HarmonyWrapper Instance => _instance ??= new();

        private readonly Harmony _harmony;
        /// <summary>
        /// Maps 'target function parameters count' to right hook function (UnifiedHook_NUMBER)
        /// </summary>
        private readonly Dictionary<string, MethodInfo> _psHooks;
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
            _psHooks = new Dictionary<string, MethodInfo>();
            var methods = typeof(HarmonyWrapper).GetMethods((BindingFlags)0xffffff);
            foreach (MethodInfo method in methods)
            {
                if (method.Name.StartsWith("UnifiedHook_"))
                {
                    string key = method.Name.Substring("UnifiedHook_".Length);
                    _psHooks[key] = method;
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


        /// <returns>Skip original</returns>
        public delegate bool HookCallback(object instance, object[] args, ref object retValue);

        public void AddHook(MethodBase target, HarmonyPatchPosition pos, HookCallback patch)
        {
            bool IsRefStruct(Type t)
            {
                var isByRefLikeProp= typeof(Type).GetProperties().FirstOrDefault(p => p.Name == "IsByRefLike");
                bool isDotNetCore = isByRefLikeProp != null;
                if(!isDotNetCore)
                    return false;

                bool isByRef = t.IsByRef;
                bool isByRefLike = (bool)isByRefLikeProp.GetValue(t);
                if(isByRefLike)
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

            //
            // Save a side the patch callback to invoke when the target is called
            //
            string uniqueId = target.DeclaringType.FullName + ":" + target.Name;
            _actualHooks[uniqueId] = patch;

            //
            // Actual hook for the method is the generic "SinglePrefixHook" (through one if its proxies 'UnifiedHook_X')
            // the "SinglePrefixHook" will search for the above saved callback and invoke it itself.
            //
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
            string binaryParamsString = string.Join(string.Empty, hookableParametersFlags.Select(i => i.ToString()));
            Console.WriteLine("");
            Logger.Debug($"[HarmonyWrapper][AddHook] Constructed this binaryParamsString: {binaryParamsString} for method {target.Name}");

            MethodInfo myPrefixHook;
            if (target.IsConstructor)
            {
                myPrefixHook = typeof(HarmonyWrapper).GetMethod("UnifiedHook_ctor", (BindingFlags)0xffff);
            }
            else
            {
                myPrefixHook = _psHooks[binaryParamsString];
            }

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
                    throw new ArgumentException("Invalid value for the 'HarmonyPatchPosition pos' arg");
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

        private static bool SinglePrefixHook(MethodBase __originalMethod, ref object __result, object __instance, params object[] args)
        {
            SmartLocksDict<MethodBase>.AcquireResults res = _locksDict.Acquire(__originalMethod);
            if(res == SmartLocksDict<MethodBase>.AcquireResults.AlreadyAcquireByCurrentThread ||
                res == SmartLocksDict<MethodBase>.AcquireResults.ThreadNotAllowedToLock
                )
            {
                // Whoops looks like we patched a method used in the 'ScubaDvier framework code'
                // Luckily, this if clause allows us to avoid recursion
                
                return false; // Don't skip original
            }

            try
            {
                string uniqueId = __originalMethod.DeclaringType.FullName + ":" + __originalMethod.Name;
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
        private static bool UnifiedHook_ctor(MethodBase __originalMethod, ref object __result) => SinglePrefixHook(__originalMethod, ref __result, new object());
        private static bool UnifiedHook_0000000000(MethodBase __originalMethod, object __instance, ref object __result) => SinglePrefixHook(__originalMethod, ref __result, __instance);
        private static bool UnifiedHook_1000000000(MethodBase __originalMethod, object __instance, object __0, ref object __result) => SinglePrefixHook(__originalMethod, ref __result, __instance, __0);
        private static bool UnifiedHook_1100000000(MethodBase __originalMethod, object __instance, object __0, object __1, ref object __result) => SinglePrefixHook(__originalMethod, ref __result, __instance, __0, __1);
        private static bool UnifiedHook_0100000000(MethodBase __originalMethod, object __instance, object __1, ref object __result) => SinglePrefixHook(__originalMethod, ref __result, __instance, DummyParameterReplacement.Instance, __1);
        private static bool UnifiedHook_1110000000(MethodBase __originalMethod, object __instance, object __0, object __1, object __2, ref object __result) => SinglePrefixHook(__originalMethod, ref __result, __instance, __0, __1, __2);
        private static bool UnifiedHook_0110000000(MethodBase __originalMethod, object __instance, object __1, object __2, ref object __result) => SinglePrefixHook(__originalMethod, ref __result, __instance, DummyParameterReplacement.Instance, __1, __2);
        private static bool UnifiedHook_1010000000(MethodBase __originalMethod, object __instance, object __0, object __2, ref object __result) => SinglePrefixHook(__originalMethod, ref __result, __instance, __0, DummyParameterReplacement.Instance, __2);
        private static bool UnifiedHook_0010000000(MethodBase __originalMethod, object __instance, object __2, ref object __result) => SinglePrefixHook(__originalMethod, ref __result, __instance, DummyParameterReplacement.Instance, DummyParameterReplacement.Instance, __2);
        private static bool UnifiedHook_1111000000(MethodBase __originalMethod, object __instance, object __0, object __1, object __2, object __3, ref object __result) => SinglePrefixHook(__originalMethod, ref __result, __instance, __0, __1, __2, __3);
        private static bool UnifiedHook_1111100000(MethodBase __originalMethod, object __instance, object __0, object __1, object __2, object __3, object __4, ref object __result) => SinglePrefixHook(__originalMethod, ref __result, __instance, __0, __1, __2, __3, __4);
        private static bool UnifiedHook_1111110000(MethodBase __originalMethod, object __instance, object __0, object __1, object __2, object __3, object __4, object __5, ref object __result) => SinglePrefixHook(__originalMethod, ref __result, __instance, __0, __1, __2, __3, __4, __5);
        private static bool UnifiedHook_1111111000(MethodBase __originalMethod, object __instance, object __0, object __1, object __2, object __3, object __4, object __5, object __6, ref object __result) => SinglePrefixHook(__originalMethod, ref __result, __instance, __0, __1, __2, __3, __4, __5, __6);
        private static bool UnifiedHook_1111111100(MethodBase __originalMethod, object __instance, object __0, object __1, object __2, object __3, object __4, object __5, object __6, object __7, ref object __result) => SinglePrefixHook(__originalMethod, ref __result, __instance, __0, __1, __2, __3, __4, __5, __6, __7);
        private static bool UnifiedHook_1111111110(MethodBase __originalMethod, object __instance, object __0, object __1, object __2, object __3, object __4, object __5, object __6, object __7, object __8, ref object __result) => SinglePrefixHook(__originalMethod, ref __result, __instance, __0, __1, __2, __3, __4, __5, __6, __7, __8);
        private static bool UnifiedHook_1111111111(MethodBase __originalMethod, object __instance, object __0, object __1, object __2, object __3, object __4, object __5, object __6, object __7, object __8, object __9, ref object __result) => SinglePrefixHook(__originalMethod, ref __result, __instance, __0, __1, __2, __3, __4, __5, __6, __7, __8, __9);
        // ReSharper restore InconsistentNaming
        // ReSharper restore UnusedMember.Local
#pragma warning restore IDE0051 // Remove unused private members
    }
}


