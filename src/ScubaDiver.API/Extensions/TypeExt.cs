using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices.ComTypes;

namespace ScubaDiver.API.Extensions
{
    public static class TypeExt
    {
        public class WildCardEnabledTypesComparer : IEqualityComparer<Type>
        {
            public bool Equals(Type x, Type y)
            {
                return x is WildCardType ||
                       y is WildCardType ||
                       x.IsAssignableFrom(y);
            }

            public int GetHashCode(Type obj)
            {
                return obj.GetHashCode();
            }
        }

        private static WildCardEnabledTypesComparer _wildCardTypesComparer = new WildCardEnabledTypesComparer();

        /// <summary>
        /// Searches a type for a specific method. If not found searches its ancestors.
        /// </summary>
        /// <param name="t">TypeFullName to search</param>
        /// <param name="methodName">Method name</param>
        /// <param name="parameterTypes">Types of parameters in the function, in order.</param>
        /// <returns></returns>
        public static MethodInfo GetMethodRecursive(this Type t, string methodName, Type[] parameterTypes)
        {

            var method = t.GetMethods((BindingFlags)0xffff)
                .SingleOrDefault(m => m.Name == methodName &&
                                      m.GetParameters().Select(pi => pi.ParameterType)
                                          .SequenceEqual(parameterTypes, _wildCardTypesComparer));
            if (method != null)
            {
                return method;
            }

            // Not found in this type...
            if (t == typeof(object))
            {
                // No more parents
                return null;
            }

            // Check parent (until `object`)
            return t.BaseType.GetMethodRecursive(methodName, parameterTypes);
        }

        /// <summary>
        /// Searches a type for a specific field. If not found searches its ancestors.
        /// </summary>
        /// <param name="t">TypeFullName to search</param>
        /// <param name="fieldName">Field name to search</param>
        public static FieldInfo GetFieldRecursive(this Type t, string fieldName)
        {
            var field = t.GetFields((BindingFlags)0xffff)
                .SingleOrDefault(fi => fi.Name == fieldName);
            if (field != null)
            {
                return field;
            }

            // Not found in this type...
            if (t == typeof(object))
            {
                // No more parents
                return null;
            }

            // Check parent (until `object`)
            return t.BaseType.GetFieldRecursive(fieldName);
        }

        public static bool IsPrimitiveEtc(this Type realType)
        {
            return realType.IsPrimitive || realType == typeof(string) || realType == typeof(decimal) ||
                    realType == typeof(DateTime);
        }

        public static Type GetType(this AppDomain domain, string typeFullName)
        {
            var assemblies = AppDomain.CurrentDomain.GetAssemblies();
            foreach (Assembly assm in assemblies)
            {
                Type t = assm.GetType(typeFullName);
                if (t != null)
                {
                    return t;
                }
            }
            return null;
        }
    }
}
