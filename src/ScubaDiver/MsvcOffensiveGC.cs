using ScubaDiver.API.Interactions.Dumps;
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

namespace ScubaDiver
{
    internal class MsvcOffensiveGC
    {
        private Dictionary<string, int> ClassSizes = new Dictionary<string, int>();

        public void Init(Dictionary<string, IEnumerable<MsvcDiver.UndecoratedExport>> types)
        {
            Logger.Debug($"[{nameof(MsvcOffensiveGC)}] {nameof(Init)} IN");
            // Find all __autoclassinit functions
            var initMethods = new Dictionary<long, MsvcDiver.UndecoratedExport>();
            var ctors = new Dictionary<string, List<MsvcDiver.UndecoratedExport>>();
            foreach (var type in types)
            {
                string fullTypeName = type.Key;
                string className = fullTypeName.Substring(fullTypeName.LastIndexOf("::") + 2);
                string ctorName = $"{fullTypeName}::{className}";
                foreach (var typeMethod in type.Value)
                {
                    // Find autoclassinit
                    if (typeMethod.UndecoratedName.Contains("autoclassinit2"))
                    {
                        Logger.Debug($"[{nameof(MsvcOffensiveGC)}] Auto Class Init 2: {typeMethod.UndecoratedName}");

                        // Multiple classes in the same module might share the autoclassinit func,
                        // so we make sure to only add it once.
                        if (!initMethods.ContainsKey(typeMethod.Export.Address))
                            initMethods[typeMethod.Export.Address] = typeMethod;
                    }

                    // Find ctor(s)
                    if (typeMethod.UndecoratedName.Contains(ctorName))
                    {
                        if (!ctors.ContainsKey(fullTypeName))
                            ctors[fullTypeName] = new List<MsvcDiver.UndecoratedExport>();
                        ctors[fullTypeName].Add(typeMethod);
                    }
                }

            }
            Logger.Debug($"[{nameof(MsvcOffensiveGC)}] Done collecting __autoclasinit2, found {initMethods.Count}");

            // Hook all __autoclassinit2
            Logger.Debug($"[{nameof(MsvcOffensiveGC)}] Starting to hook __autoclassinit2...");
            foreach (var kvp in initMethods)
            {
                Logger.Debug($"[{nameof(MsvcOffensiveGC)}] Hooking {kvp.Value.UndecoratedName}");
                //MethodInfo mi = typeof(MsvcOffensiveGC).GetMethod(nameof(AutoInit2));
                // TODO: will probably break just like ctors broke
                //DetoursNetWrapper.Instance.AddHook(kvp.Value, HarmonyPatchPosition.Prefix, typeof(AutoInit2Type), mi);
            }
            Logger.Debug($"[{nameof(MsvcOffensiveGC)}] Done hooking __autoclassinit2.");
            Logger.Debug($"[{nameof(MsvcOffensiveGC)}] DelegateStore.Mine.Count: {DelegateStore.Mine.Count}");

            // Hook all ctors
            Logger.Debug($"[{nameof(MsvcOffensiveGC)}] Starting to hook ctors...");
            int attemptedHookedCtorsCount = 0;
            foreach (var kvp in ctors)
            {
                if (_forbiddenClasses.Contains(kvp.Key))
                {
                    Logger.Debug($"[{nameof(MsvcOffensiveGC)}] SKIPPING FORBIDDEN CLASS: " + kvp.Key);
                    continue;
                }

                foreach (var ctor in kvp.Value)
                {
                    MsMangledNameParser parser = new MsMangledNameParser(ctor.Export.Name);
                    string basicName;
                    SerializedType sig = null;
                    SerializedType enclosingType;
                    try
                    {
                        (basicName, sig, enclosingType) = parser.Parse();
                    }
                    catch (Exception ex)
                    {
                        Logger.Debug($"Failed to demangle ctor of {kvp.Key}, Raw: {ctor.Export.Name}, Exception: " + ex);
                        continue;

                    }

                    Argument_v1[] args = (sig as SerializedSignature)?.Arguments;
                    if (args == null)
                    {
                        // Failed to parse?!?
                        Logger.Debug($"Failed to parse arguments from ctor of {kvp.Key}, Raw: {ctor.Export.Name}");
                        continue;
                    }


                    if (args.Length == 0)
                    {
                        var (mi, delegateValue) = GenerateMethodsForSecret(ctor.UndecoratedName);
                        DetoursNetWrapper.Instance.AddHook(ctor, HarmonyPatchPosition.Prefix, typeof(GenericCtorType), mi, delegateValue);
                        attemptedHookedCtorsCount++;
                    }
                }
            }

            Logger.Debug($"[{nameof(MsvcOffensiveGC)}] Done hooking ctors. attempted to hook: {attemptedHookedCtorsCount} ctors");
            Logger.Debug($"[{nameof(MsvcOffensiveGC)}] Done hooking ctors. DelegateStore.Mine.Count: {DelegateStore.Mine.Count}");


            Logger.Debug($"[{nameof(MsvcOffensiveGC)}] {nameof(Init)} OUT");
        }

        //public delegate void AutoInit2Type(ulong self, ulong size);

        //public static void AutoInit2(ulong self, ulong size)
        //{
        //    Logger.Debug($"[AutoInit2] Address: 0x{self:x16} Size: {size}");
        //}


        public delegate ulong GenericCtorType(ulong self);


        public IReadOnlyDictionary<string, int> GetFindings() => ClassSizes;


        // Cache for generated methods (Also used so they aren't GC'd)
        private static Dictionary<string, (MethodInfo, Delegate)> _cached = new Dictionary<string, (MethodInfo, Delegate)>();

        public static (MethodInfo, Delegate) GenerateMethodsForSecret(string secret)
        {
            if (_cached.TryGetValue(secret, out (MethodInfo, Delegate) existing))
                return existing;

            // Get the method info of the UnifiedMethod
            var unifiedMethodInfo = typeof(MsvcOffensiveGC).GetMethod("UnifiedMethod",
                BindingFlags.Public | BindingFlags.Static);

            // Generate methods for each character in "secret"
            // Create a dynamic method with the desired signature
            var dynamicMethod = new DynamicMethod(
                "GeneratedMethod_" + secret,
                typeof(ulong),
                new Type[] { typeof(ulong) },
                typeof(MsvcOffensiveGC)
            );

            // Generate IL for the dynamic method
            var ilGenerator = dynamicMethod.GetILGenerator();
            ilGenerator.Emit(OpCodes.Ldstr, secret);         // Load the secret string onto the stack
            ilGenerator.Emit(OpCodes.Ldarg_0);               // Load the self argument onto the stack
            ilGenerator.Emit(OpCodes.Call, unifiedMethodInfo);  // Call the UnifiedMethod
            ilGenerator.Emit(OpCodes.Ret);                   // Return from the method

            // Create a delegate for the dynamic method
            Delegate delegateInstance = dynamicMethod.CreateDelegate(typeof(GenericCtorType));
            MethodInfo mi = delegateInstance.Method;

            _cached[secret] = (mi, delegateInstance);
            return (mi, delegateInstance);
        }

        private static ThreadLocal<int> counter = new ThreadLocal<int>(() => 0);
        private List<string> _forbiddenClasses = new List<string>()
        {
            "SPen::EndTag",
            "SPen::File",
            "SPen::GestureFactoryListener",
            "SPen::AnimatorUpdateManager",
            "SPen::AnimatorUpdateManagerEventListener",
            "SPen::DrawLoopSwapChain",
            "SPen::DrawLoop",
            "SPen::IInvalidatable",
            "SPen::Color",
            "SPen::IColorTheme",
        };

        public static ulong UnifiedMethod(string secret, ulong self)
        {
            ulong res = 0x0bad_c0de_dead_c0de;
            int hashcode = 0x00000000;
            void LogIndented(string prefix, int indent, string msg)
            {
                string indentation = new string(' ', indent * 2);
                string indentedMsg = prefix + indentation + msg;
                Logger.Debug(indentedMsg);
            }
            counter.Value++; // Increment the counter on entry

            LogIndented($"[UnifiedMethod][HC={hashcode:x8}] ", counter.Value, "NEW CTOR: " + secret);
            LogIndented($"[UnifiedMethod][HC={hashcode:x8}] ", counter.Value,$"Self: 0x{self:x16}");

            LogIndented($"[UnifiedMethod][HC={hashcode:x8}] ", counter.Value, "Seeking MethodInfo of dynamic method");
            if (_cached.ContainsKey(secret))
            {
                var (originalHookMethodInfo, _) = _cached[secret];
                hashcode = originalHookMethodInfo.GetHashCode();
                LogIndented($"[UnifiedMethod][HC={hashcode:x8}] ", counter.Value,
                    $"Found MethodInfo of dynamic method. Hashcode: {hashcode}");

                LogIndented($"[UnifiedMethod][HC={hashcode:x8}] ", counter.Value, "Seeking delegate for detour'd method.");
                if (DelegateStore.Real.ContainsKey(originalHookMethodInfo))
                {
                    LogIndented($"[UnifiedMethod][HC={hashcode:x8}] ", counter.Value,
                        $"Found delegate for detour'd method.");

                    var originalMethod = (GenericCtorType)DelegateStore.Real[originalHookMethodInfo];
                    // Invoking original ctor
                    LogIndented($"[UnifiedMethod][HC={hashcode:x8}] ", counter.Value, $"Invoking original ctor");
                    res = originalMethod(self);
                    LogIndented($"[UnifiedMethod][HC={hashcode:x8}] ", counter.Value, $"Invoking completed! res: {res}");
                }
                else
                {
                    LogIndented("[UnifiedMethod][HC={hashcode:x8}] ", counter.Value,
                        $"FATAL ERROR. Couldn't find detour'd method for MethodInfo of '{secret}'");
                }
            }
            else
            {
                LogIndented("[UnifiedMethod][HC={hashcode:x8}] ", counter.Value, $"FATAL ERROR. Couldn't find origina for '{secret}'");
            }
            counter.Value--;
            return res;
        }
    }
}
