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

        private Assembly[] _assemblies = null;

        public Assembly[] GetAssemblies()
        {
            if (_assemblies == null)
            {
                // Using Diver's heap searching abilities to locate all 'RuntimeAssemblies'
                try
                {
                    (bool anyErrors, var candidates) = _parentDiver.GetHeapObjects(heapObjType => heapObjType == "System.Reflection.RuntimeAssembly");

                    if(anyErrors)
                    {
                        throw new Exception("GetHeapObjects returned anyErrors=True");
                    }

                    _assemblies = candidates.Select(cand => _parentDiver.GetObject(cand.Address, false, cand.HashCode).instance).Cast<Assembly>().ToArray();
                    Logger.Debug("[Diver][Assemblies Finder] All assemblies were retrieved from all AppDomains :)");
                }
                catch (Exception ex)
                {
                    Logger.Debug("[Diver][Assemblies Finder] Failed to search heap for Runtime Assemblies. Error: " + ex.Message);

                    // Fallback - Just return all assemblies in the current AppDomain. It's not ALL of them but sometimes it's good enough.
                    _assemblies = AppDomain.CurrentDomain.GetAssemblies();
                }
            }
            return _assemblies;
        }

        public Type ResolveType(string typeFullName, string assembly = null)
        {
            foreach (Assembly assm in this.GetAssemblies())
            {
                Type t = assm.GetType(typeFullName, throwOnError: false);
                if (t != null)
                {
                    Logger.Debug($"[Diver][TypesResolver] Resolved type with reflection in assembly: {assm.FullName}");
                    return t;
                }
            }
            throw new Exception("Could not find type in any of the known assemblies");
        }
    }
}
