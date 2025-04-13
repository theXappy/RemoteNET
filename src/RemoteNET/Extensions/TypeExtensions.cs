using System;
using System.Linq;
using System.Reflection;

namespace RemoteNET.Extensions
{
    public static class TypeExtensions
    {
        /// <summary>
        /// Retrieves a method from the specified type that matches the given name, binding flags, and parameter types.
        /// </summary>
        /// <param name="type">The type to search for the method.</param>
        /// <param name="name">The name of the method.</param>
        /// <param name="bindingAttr">The binding flags to use for the search.</param>
        /// <param name="types">The parameter types of the method.</param>
        /// <returns>The matching MethodInfo, or null if no match is found.</returns>
        public static MethodInfo GetMethod(this Type type, string name, BindingFlags bindingAttr, Type[] types)
        {
            // Use the existing GetMethod overload with a Binder
            var methods = type.GetMethods(bindingAttr)
                              .Where(m => m.Name == name &&
                                          m.GetParameters().Select(p => p.ParameterType).SequenceEqual(types));

            return methods.FirstOrDefault();
        }
    }
}
