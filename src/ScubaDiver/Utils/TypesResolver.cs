using Microsoft.Diagnostics.Runtime;
using ScubaDiver.API.Extensions;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;

namespace ScubaDiver.Utils
{
    public static class TypesResolver
    {
        public static Type Resolve(ClrRuntime _runtime, string name, string assembly = null)
        {
            if (string.IsNullOrEmpty(name))
            {
                Debugger.Launch();
            }

            // TODO: With .NET Core divers thre seems to be some infinte loop when trying to resolve System.Int32 so
            // this hack fixes it for now
            if (name == "System.Int32")
            {
                return typeof(int);
            }
            if (name.StartsWith("System.Span`1[[System.Char,"))
            {
                return typeof(Span<Char>);
            }


            IList<ClrModule> assembliesToSearch = _runtime.AppDomains.First().Modules;
            if (assembly != null)
                assembliesToSearch = assembliesToSearch.Where(mod => Path.GetFileNameWithoutExtension(mod.Name) == assembly).ToList();
            if (!assembliesToSearch.Any())
            {
                // No such assembly
                Logger.Debug($"[Diver] No such assembly \"{assembly}\"");
                return null;
            }

            foreach (ClrModule module in assembliesToSearch)
            {
                ClrType clrTypeInfo = module.GetTypeByName(name);
                if (clrTypeInfo == null)
                {
                    var x = module.OldSchoolEnumerateTypeDefToMethodTableMap();
                    var typeNames = (from tuple in x
                                     let token = tuple.Token
                                     let resolvedType = module.ResolveToken(token) ?? null
                                     where resolvedType?.Name == name
                                     select new { MethodTable = tuple.MethodTable, Token = token, ClrType = resolvedType }).ToList();
                    if (typeNames.Any())
                    {
                        clrTypeInfo = typeNames.First().ClrType;
                    }
                }

                if (clrTypeInfo == null)
                {
                    continue;
                }

                // Found it
                Type typeObj = clrTypeInfo.GetRealType();
                return typeObj;
            }

            // Fallback - normal .NET reflection
            foreach (Assembly assm in AppDomain.CurrentDomain.GetAssemblies())
            {
                Type t = assm.GetType(name, throwOnError: false);
                if (t != null)
                {
                    return t;
                }
            }

            return null;
        }
    }
}
