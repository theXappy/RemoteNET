using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace RemoteNET.Internal.Reflection
{
    /// <summary>
    /// Resolves local and remote types. Contains a cache so the same TypeFullName object is returned for different
    /// resolutions for the same remote type.
    /// </summary>
    public class TypesResolver
    {
        // Since the resolver works with a cache that should be global we make the whole class a singleton
        public static TypesResolver Instance = new TypesResolver();

        private TypesResolver() { }

        private readonly Dictionary<Tuple<string, string>, Type> _cache = new Dictionary<Tuple<string, string>, Type>();

        public void RegisterType(string assemblyName, string typeFullName, Type type)
        {
            _cache[new Tuple<string, string>(assemblyName, typeFullName)] = type;
        }

        public Type Resolve(string assemblyName, string typeFullName)
        {
            // Start by searching cache
            if (_cache.TryGetValue(new Tuple<string, string>(assemblyName, typeFullName), out Type resolvedType))
            {
                return resolvedType;
            }

            // Search for locally available types
            var assemblies = AppDomain.CurrentDomain.GetAssemblies();
            foreach (Assembly assembly in assemblies.Where(assm => assm.FullName.Contains(assemblyName)))
            {
                resolvedType = assembly.GetType(typeFullName);
            }

            if (resolvedType != null)
            {
                RegisterType(assemblyName, typeFullName, resolvedType);
            }
            return resolvedType;
        }
    }
}
