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
        private readonly Diver _parentDiver;

        /// <summary>
        /// Parent diver, which is currently running in the app
        /// </summary>
        /// <param name="parentDiver"></param>
        public UnifiedAppDomain(Diver parentDiver)
        {
            _parentDiver = parentDiver;
        }

        private AppDomain[] _domains;

        public AppDomain[] GetDomains()
        {
            if (_domains == null)
            {
                // Using Diver's heap searching abilities to locate all 'System.AppDomain'
                try
                {
                    (bool anyErrors, var candidates) = _parentDiver.GetHeapObjects(heapObjType => heapObjType == typeof(AppDomain).FullName, true);

                    if(anyErrors)
                    {
                        throw new Exception("GetHeapObjects returned anyErrors: True");
                    }

                    _domains = candidates.Select(cand => _parentDiver.GetObject(cand.Address, false, cand.HashCode).instance).Cast<AppDomain>().ToArray();
                    Logger.Debug("[Diver][UnifiedAppDomain] All assemblies were retrieved from all AppDomains :)");
                }
                catch (Exception ex)
                {
                    Logger.Debug("[Diver][UnifiedAppDomain] Failed to search heap for Runtime Assemblies. Error: " + ex.Message);

                    // Fallback - Just return all assemblies in the current AppDomain. Obviously, it's not ALL of them but sometimes it's good enough.
                    _domains = new[] { AppDomain.CurrentDomain };
                }
            }
            return _domains;
        }

        public Assembly[] GetAssemblies()
        {
            return GetDomains().SelectMany(domain => domain.GetAssemblies()).ToArray();
        }

        public Type ResolveType(string typeFullName, string assembly = null)
        {
            if (typeFullName.Contains('<') && typeFullName.EndsWith(">"))
            {
                string genericParams = typeFullName.Substring(typeFullName.LastIndexOf('<'));
                int numOfParams = genericParams.Split(',').Length;

                string nonGenericPart = typeFullName.Substring(0,typeFullName.LastIndexOf('<'));
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
            throw new Exception("Could not find type in any of the known assemblies");
        }
    }
}
