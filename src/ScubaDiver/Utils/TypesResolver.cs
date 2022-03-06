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


            foreach (var appDom in _runtime.AppDomains)
            {
                try
                {
                    IList<ClrModule> assembliesToSearch = appDom.Modules;
                    if (assembly != null)
                        assembliesToSearch = assembliesToSearch.Where(mod => Path.GetFileNameWithoutExtension(mod.Name) == assembly).ToList();

                    foreach (ClrModule module in assembliesToSearch)
                    {
                        ClrType clrTypeInfo = module.GetTypeByName(name);
                        if (clrTypeInfo == null)
                        {
                            var x = module.OldSchoolEnumerateTypeDefToMethodTableMap();
                            var typeNames = (from tuple in x
                                             let Token = tuple.Token
                                             let ClrType = module.ResolveToken(Token) ?? null
                                             where ClrType?.Name == name
                                             select new { tuple.MethodTable, Token, ClrType }).ToList();
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
                        Logger.Debug($"[Diver][TypesResolver] Resolved type from ClrMD Dump in domain: {appDom.Name}");
                        Type typeObj = clrTypeInfo.GetRealType();
                        return typeObj;
                    }
                }
                catch
                {
                    // Using ClrMD Failed but we have a fallback
                }
            }

            // Fallback - normal .NET reflection in all assemblies of all domains
            // BEWARE: Confitional compilation 🤢
            // .NET Core deprecated "App Domains" as a concept
            // Which means the AppDomain class still exists but the "_AppDomain" struct isn't...
            // In a sense, .NET Core's only possible AppDomain instance is the one representing the current (and only) App Domain in the app.
#if NETCOREAPP
            foreach (AppDomain domain in CLRUtil.EnumAppDomains())
#else
            foreach (_AppDomain domain in CLRUtil.EnumAppDomains())
#endif
            { 
                foreach (Assembly assm in domain.GetAssemblies())
                {
                    Type t = assm.GetType(name, throwOnError: false);
                    if (t != null)
                    {
                        Logger.Debug($"[Diver][TypesResolver] Resolved type with reflection in domain: {domain.FriendlyName}");
                        return t;
                    }
                }
            }

            // Special case for List<T>
            if (name.StartsWith("System.Collections.Generic.List`1"))
            {
                string innerType = name.Substring("System.Collections.Generic.List`1".Length).Trim('[').Trim(']');
                try
                {
                    Type inner = TypesResolver.Resolve(_runtime, innerType);
                    if (inner != null)
                    {
                        var listType = typeof(List<>);
                        var constructedListType = listType.MakeGenericType(inner);
                        if(constructedListType.FullName == name)
                            return constructedListType;
                    }
                }
                catch { }
            }

            return null;
        }
    }
}
