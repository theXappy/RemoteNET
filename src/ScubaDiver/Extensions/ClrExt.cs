using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using Microsoft.Diagnostics.Runtime;

namespace ScubaDiver.Extensions
{
    public static class ClrExt
    {
        public static byte[] ToByteArray(this ClrArray arr)
        {
            try
            {
                arr.GetValue<byte>(0);
            }
            catch (Exception ex)
            {
                // ???
                throw new ArgumentException("Not a byte array", ex);
            }

            byte[] res = new byte[arr.Length];
            for (int i = 0; i < res.Length; i++)
            {
                res[i] = arr.GetValue<byte>(i);
            }
            return res;
        }

        public static byte[] ToByteArray(this ClrObject obj)
        {
            return obj.AsArray().ToByteArray();
        }

        /// <summary>
        /// Finds the `TypeFullName` object matching the requested ClrType.
        /// </summary>
        /// <param name="domain">Optional domain to search. If not specified current domain is searched</param>
        /// <returns>Matching TypeFullName or null if not found</returns>
        public static Type GetRealType(this ClrType type, AppDomain domain = null)
        {
            if (domain == null) domain = AppDomain.CurrentDomain;

            var fullTypeName = type.Name;
            var assemblyNamePrefix = Path.GetFileNameWithoutExtension(type.Module.AssemblyName);
            foreach (var assembly in domain.GetAssemblies())
            {
                string assmName = assembly.GetName().Name;
                if (assmName.StartsWith(assemblyNamePrefix) || (assmName == "mscorlib" && assemblyNamePrefix.StartsWith("System")))
                {
                    Type match = assembly.GetType(fullTypeName);
                    if (match != null)
                    {
                        // Found it!
                        return match;
                    }
                }
            }

            return null;
        }

        public class TypeDefToMethod
        {
            public ulong MethodTable { get; set; }
            public int Token { get; set; }
        }
        public static IEnumerable<TypeDefToMethod> OldSchoolEnumerateTypeDefToMethodTableMap(this ClrModule mod)
        {
            //EnumerateTypeDefToMethodTableMap wants to return an IEnumerable<(ulong,int)> to us but returning tuples costs
            //us another dependency so we're avoiding it.
            IEnumerable misteriousEnumerable = typeof(ClrModule).GetMethod("EnumerateTypeDefToMethodTableMap").Invoke(mod, new object[0]) as IEnumerable;
            foreach (object o in misteriousEnumerable)
            {
                var type = o.GetType();
                ulong mt = (ulong)type.GetField("Item1").GetValue(o);
                int token = (int)type.GetField("Item2").GetValue(o);
                yield return new TypeDefToMethod() { MethodTable = mt, Token = token };
            }
        }
    }
}