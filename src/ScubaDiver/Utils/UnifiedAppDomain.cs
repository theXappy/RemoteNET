using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;

namespace ScubaDiver
{
    /// <summary>
    /// Encapsulates access to all AppDomains in the process
    /// </summary>
    public class UnifiedAppDomain
    {
        // NOTE: Under the hhok this app doesn't really have access to all 'AppDomain' objects
        // because those are very hard to retrieve.
        // Instead this class steals all instances of 'RuntimeAssembly' from the heap, which is actually all the assemblies from all the App Domains.

        private Diver _parentDiver;

        /// <summary>
        /// Parent diver, which is currently running in the app
        /// </summary>
        /// <param name="parentDiver"></param>
        public UnifiedAppDomain(Diver parentDiver)
        {
            _parentDiver = parentDiver;
        }

        private AppDomain[] _domains = null;

        public AppDomain[] GetDomains()
        {
            if (_domains == null)
            {
                // Using Diver's heap searching abilities to locate all 'RuntimeAssemblies'
                try
                {
                    (bool anyErrors, var candidates) = _parentDiver.GetHeapObjects(heapObjType => heapObjType == typeof(AppDomain).FullName);

                    if(anyErrors)
                    {
                        throw new Exception("GetHeapObjects returned anyErrors=True");
                    }

                    _domains = candidates.Select(cand => _parentDiver.GetObject(cand.Address, false, cand.HashCode).instance).Cast<AppDomain>().ToArray();
                    Logger.Debug("[Diver][UnifiedAppDomain] All assemblies were retrieved from all AppDomains :)");
                }
                catch (Exception ex)
                {
                    Logger.Debug("[Diver][UnifiedAppDomain] Failed to search heap for Runtime Assemblies. Error: " + ex.Message);

                    // Fallback - Just return all assemblies in the current AppDomain. It's not ALL of them but sometimes it's good enough.
                    _domains = new AppDomain[1] { AppDomain.CurrentDomain };
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
            foreach (Assembly assm in this.GetAssemblies())
            {
                Type t = assm.GetType(typeFullName, throwOnError: false);
                if (t != null)
                {
                    Logger.Debug($"[Diver][UnifiedAppDomain.ResolveType] Resolved type with reflection in assembly: {assm.FullName}");
                    return t;
                }
            }
            throw new Exception("Could not find type in any of the known assemblies");
        }
    }
}
