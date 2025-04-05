using System;
using System.Linq;
using System.Reflection;

namespace ScubaDiver.Utils
{
    /// <summary>
    /// Encapsulates access to all AppDomains in the process
    /// </summary>
    public class UnifiedAppDomain
    {
        private readonly DotNetDiver _parentDiver;

        /// <summary>
        /// Parent diver, which is currently running in the app
        /// </summary>
        /// <param name="parentDiver"></param>
        public UnifiedAppDomain(DotNetDiver parentDiver)
        {
            _parentDiver = parentDiver;
        }

        private AppDomain[] _domains;

        public AppDomain[] GetDomains()
        {
            if (_domains == null)
            {
                bool useFallback = false;

                // Using DotNetDiver's heap searching abilities to locate all 'System.AppDomain'
                try
                {
                    (bool anyErrors, var candidates) = _parentDiver.GetHeapObjects(heapObjType => heapObjType == typeof(AppDomain).FullName, true);

                    if (anyErrors)
                    {
                        throw new Exception("GetHeapObjects returned anyErrors: True");
                    }

                    _domains = candidates
                        .Select(cand => _parentDiver.GetObject(cand.Address, false, cand.Type, cand.HashCode).instance)
                        .Cast<AppDomain>().ToArray();
                    Logger.Debug("[DotNetDiver][UnifiedAppDomain] All assemblies were retrieved from all AppDomains :)");

                    // Check for failures
                    if (_domains.Length == 0)
                    {
                        Console.WriteLine("[DotNetDiver][UnifiedAppDomain] WARNING searching the heap for System.AppDomains returned nothing.");
                        useFallback = true;
                    }
                }
                catch (Exception ex)
                {
                    Logger.Debug("[DotNetDiver][UnifiedAppDomain] Failed to search heap for Runtime Assemblies. Error: " + ex.Message);
                    useFallback = true;
                }

                // if we failed to find app domains in the sneaky way, use formal .NET APIs to at least get OUR domain.
                if (useFallback)
                    _domains = new[] { AppDomain.CurrentDomain };
            }
            return _domains;
        }

        public Assembly[] GetAssemblies()
        {
            var domains = GetDomains();
            var allAssemblies = domains.SelectMany(domain => domain.GetAssemblies());
            // Like `Distinct` without an IEqualityComparer
            var uniqueAssemblies = allAssemblies.GroupBy(x => x.FullName).Select(grp => grp.First());
            return uniqueAssemblies.ToArray();
        }

        public Type ResolveType(string typeFullName, string assembly = null)
        {
            // TODO: Nullable gets a special case but in general we should switch to a recursive type-resolution.
            // So stuff like: Dictionary<FirstAssembly.FirstType, SecondAssembly.SecondType> will always work
            if (typeFullName.StartsWith("System.Nullable`1[["))
            {
                return ResolveNullableType(typeFullName, assembly);
            }

            if (typeFullName.Contains('<') && typeFullName.EndsWith(">"))
            {
                string genericParams = typeFullName.Substring(typeFullName.LastIndexOf('<'));
                int numOfParams = genericParams.Split(',').Length;

                string nonGenericPart = typeFullName.Substring(0, typeFullName.LastIndexOf('<'));
                // TODO: Does this event work? it turns List<int> and List<string> both to List`1?
                typeFullName = $"{nonGenericPart}`{numOfParams}";
            }


            foreach (Assembly assm in GetAssemblies())
            {
                Type t = assm.GetType(typeFullName, throwOnError: false);
                if (t != null)
                {
                    return t;
                }
            }
            throw new Exception($"Could not find type '{typeFullName}' in any of the known assemblies");
        }

        private Type ResolveNullableType(string typeFullName, string assembly)
        {
            // Remove prefix: "System.Nullable`1[["
            string innerTypeName = typeFullName.Substring("System.Nullable`1[[".Length);
            // Remove suffix: "]]"
            innerTypeName = innerTypeName.Substring(0, innerTypeName.Length - 2);
            // Type name is everything before the first comma (affter that we have some assembly info)
            innerTypeName = innerTypeName.Substring(0, innerTypeName.IndexOf(',')).Trim();

            Type innerType = ResolveType(innerTypeName);
            if (innerType == null)
                return null;

            Type nullable = typeof(Nullable<>);
            return nullable.MakeGenericType(innerType);
        }
    }
}
