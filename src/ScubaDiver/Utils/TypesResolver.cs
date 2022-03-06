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

            // Just searching in all app domains and all assemblies the given name
            var realType = ClrExt.GetRealType(name, assembly);
            if(realType != null)
            {
                return realType;
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
