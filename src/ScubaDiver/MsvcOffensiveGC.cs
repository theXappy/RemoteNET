using ScubaDiver.API.Interactions.Dumps;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using ScubaDiver.API.Hooking;
using ScubaDiver.Hooking;

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
                bool foundInitFunc = false;
                bool foundCtor = false;
                foreach (var typeMethod in type.Value)
                {
                    // Find autoclassinit
                    if (typeMethod.UndecoratedName.Contains("autoclassinit2"))
                    {
                        Logger.Debug($"[{nameof(MsvcOffensiveGC)}] Auto Class Init 2: {typeMethod.UndecoratedName}");
                        foundInitFunc = true;

                        // Multiple classes in the same module might share the autoclassinit func,
                        // so we make sure to only add it once.
                        if (!initMethods.ContainsKey(typeMethod.Export.Address))
                            initMethods[typeMethod.Export.Address] = typeMethod;
                    }

                    // Find ctor(s)
                    if (typeMethod.UndecoratedName.Contains(ctorName))
                    {
                        foundCtor = true;

                        if (!ctors.ContainsKey(fullTypeName))
                            ctors[fullTypeName] = new List<MsvcDiver.UndecoratedExport>();
                        ctors[fullTypeName].Add(typeMethod);
                    }
                }

                if (!foundInitFunc)
                    Logger.Debug($"[{nameof(MsvcOffensiveGC)}] Could not find autoclass init func for {fullTypeName}");
                if (!foundCtor)
                    Logger.Debug($"[{nameof(MsvcOffensiveGC)}] Could not find any ctors for {fullTypeName}");
            }
            Logger.Debug($"[{nameof(MsvcOffensiveGC)}] Done collecting __autoclasinit2, found {initMethods.Count}");

            // Hook all __autoclassinit2
            Logger.Debug($"[{nameof(MsvcOffensiveGC)}] Starting to hook __autoclassinit2...");
            foreach (var kvp in initMethods)
            {
                Logger.Debug($"[{nameof(MsvcOffensiveGC)}] Hooking {kvp.Value.UndecoratedName}");
                MethodInfo mi = typeof(MsvcOffensiveGC).GetMethod(nameof(AutoInit2));
                DetoursNetWrapper.Instance.AddHook(kvp.Value, HarmonyPatchPosition.Prefix, mi, typeof(AutoInit2Type));
            }
            Logger.Debug($"[{nameof(MsvcOffensiveGC)}] Done hooking __autoclassinit2.");

            // Hook all ctors
            Logger.Debug($"[{nameof(MsvcOffensiveGC)}] Starting to hook ctors...");
            foreach (var kvp in ctors)
            {
                foreach (var ctor in kvp.Value)
                {
                    Logger.Debug($"[{nameof(MsvcOffensiveGC)}] Hooking {ctor.UndecoratedName}");
                    MethodInfo mi = typeof(MsvcOffensiveGC).GetMethod(nameof(GenericCtor));
                    DetoursNetWrapper.Instance.AddHook(ctor, HarmonyPatchPosition.Prefix, mi, typeof(GenericCtorType));
                }
            }
            Logger.Debug($"[{nameof(MsvcOffensiveGC)}] Done hooking ctors.");


            Logger.Debug($"[{nameof(MsvcOffensiveGC)}] {nameof(Init)} OUT");
        }

        public delegate void AutoInit2Type(ulong self, ulong size);

        public static void AutoInit2(ulong self, ulong size)
        {
            Console.WriteLine($"[AutoInit2] Address: 0x{self:x16} Size: {size}");
        }


        public delegate void GenericCtorType(ulong self);
        public static void GenericCtor(ulong self)
        {
            Console.WriteLine($"[Ctor] Address: 0x{self:x16}");
        }


        public IReadOnlyDictionary<string, int> GetFindings() => ClassSizes;
    }
}
