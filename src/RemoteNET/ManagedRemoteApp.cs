using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using RemoteNET.Common;
using RemoteNET.Internal;
using RemoteNET.Internal.Reflection.DotNet;
using ScubaDiver.API;
using ScubaDiver.API.Interactions.Dumps;
using ScubaDiver.API.Utils;

namespace RemoteNET
{
    public class ManagedRemoteApp : RemoteApp
    {
        internal class RemoteObjectsCollection
        {
            // The WeakReferences are to RemoteObject
            private readonly Dictionary<ulong, WeakReference<ManagedRemoteObject>> _pinnedAddressesToRemoteObjects;
            private readonly object _lock = new object();

            private readonly ManagedRemoteApp _app;

            public RemoteObjectsCollection(ManagedRemoteApp app)
            {
                _app = app;
                _pinnedAddressesToRemoteObjects = new Dictionary<ulong, WeakReference<ManagedRemoteObject>>();
            }

            private ManagedRemoteObject GetRemoteObjectUncached(ulong remoteAddress, string typeName, int? hashCode = null)
            {
                ObjectDump od;
                TypeDump td;
                try
                {
                    od = _app._managedCommunicator.DumpObject(remoteAddress, typeName, true, hashCode);
                    td = _app._managedCommunicator.DumpType(od.Type);
                }
                catch (Exception e)
                {
                    throw new Exception("Could not dump remote object/type.", e);
                }


                var remoteObject = new ManagedRemoteObject(new RemoteObjectRef(od, td, _app._managedCommunicator), _app);
                return remoteObject;
            }

            public ManagedRemoteObject GetRemoteObject(ulong address, string typeName, int? hashcode = null)
            {
                ManagedRemoteObject ro;
                WeakReference<ManagedRemoteObject> weakRef;
                // Easiert way - Non-collected and previouslt obtained object ("Cached")
                if (_pinnedAddressesToRemoteObjects.TryGetValue(address, out weakRef) &&
                    weakRef.TryGetTarget(out ro))
                {
                    // Not GC'd!
                    return ro;
                }

                // Harder case - At time of checking, item wasn't cached.
                // We need exclusive access to the cahce now to make sure we are the only one adding it.
                lock (_lock)
                {
                    // Last chance - when we waited on the lock some other thread might've added it to the cache.
                    if (_pinnedAddressesToRemoteObjects.TryGetValue(address, out weakRef))
                    {
                        bool gotTarget = weakRef.TryGetTarget(out ro);
                        if (gotTarget)
                        {
                            // Not GC'd!
                            return ro;
                        }
                        else
                        {
                            // Object was GC'd...
                            _pinnedAddressesToRemoteObjects.Remove(address);
                            // Now let's make sure the GC'd object finalizer was also called (otherwise some "object moved" errors might happen).
                            GC.WaitForPendingFinalizers();
                            // Now we need to-read the remote object since stuff might have moved
                        }
                    }

                    // Get remote
                    ro = this.GetRemoteObjectUncached(address, typeName, hashcode);
                    // Add to cache
                    weakRef = new WeakReference<ManagedRemoteObject>(ro);
                    _pinnedAddressesToRemoteObjects[ro.RemoteToken] = weakRef;
                }

                return ro;
            }
        }

        private Process _procWithDiver;
        private DiverCommunicator _managedCommunicator;
        private DomainsDump _managedDomains;
        private readonly ManagedRemoteApp.RemoteObjectsCollection _remoteObjects;

        private RemoteHookingManager _hookingManager;
        public override RemoteHookingManager HookingManager => _hookingManager;


        public Process Process => _procWithDiver;
        public RemoteActivator Activator { get; private set; }
        public RemoteMarshal Marshal { get; private set; }

        public override DiverCommunicator Communicator => _managedCommunicator;

        public ManagedRemoteApp(Process procWithDiver, DiverCommunicator managedCommunicator)
        {
            _procWithDiver = procWithDiver;
            _managedCommunicator = managedCommunicator;
            Activator = new RemoteActivator(managedCommunicator, this);
            Marshal = new RemoteMarshal(this);
            _hookingManager = new RemoteHookingManager(this);
            _remoteObjects = new ManagedRemoteApp.RemoteObjectsCollection(this);
        }

        //
        // Init
        // 

        //
        // Remote Heap querying
        //

        public override IEnumerable<CandidateType> QueryTypes(string typeFullNameFilter)
        {
            Predicate<string> matchesFilter = Filter.CreatePredicate(typeFullNameFilter);

            _managedDomains ??= _managedCommunicator.DumpDomains();
            var allModules = _managedDomains.AvailableDomains.SelectMany(domain => domain.AvailableModules);
            foreach (string assembly in allModules)
            {
                List<TypesDump.TypeIdentifiers> typeIdentifiers;
                try
                {
                    typeIdentifiers = _managedCommunicator.DumpTypes(assembly).Types;
                }
                catch
                {
                    // TODO:
                    Debug.WriteLine($"[{nameof(ManagedRemoteApp)}][{nameof(QueryTypes)}] Exception thrown when Dumping/Iterating managed assembly: {assembly}");
                    continue;
                }
                foreach (TypesDump.TypeIdentifiers type in typeIdentifiers)
                {
                    // TODO: Filtering should probably be done in the Diver's side
                    if (matchesFilter(type.TypeName))
                        yield return new CandidateType(RuntimeType.Managed, type.TypeName, assembly);
                }

            }
        }

        /// <summary>
        /// Gets all object candidates for a specific filter
        /// </summary>
        /// <param name="typeFullNameFilter">Objects with Full Type Names of this EXACT string will be returned. You can use '*' as a "0 or more characters" wildcard</param>
        /// <param name="dumpHashcodes">Whether to also dump hashcodes of every matching object.
        /// This makes resolving the candidates later more reliable but for wide queries (e.g. "*") this might fail the entire search since it causes instabilities in the heap when examining it.
        /// </param>
        public override IEnumerable<CandidateObject> QueryInstances(string typeFullNameFilter, bool dumpHashcodes = true)
        {
            var managedHeapDump = _managedCommunicator.DumpHeap(typeFullNameFilter, dumpHashcodes);
            var managedCandidates = managedHeapDump.Objects.Select(heapObj => new CandidateObject(RuntimeType.Managed, heapObj.Address, heapObj.Type, heapObj.HashCode));
            return managedCandidates;
        }

        //
        // Resolving Types
        //

        /// <summary>
        /// Gets a handle to a remote type (even ones from assemblies we aren't referencing/loading to the local process)
        /// </summary>
        /// <param name="typeFullName">Full name of the type to get. For example 'System.Xml.XmlDocument'</param>
        /// <param name="assembly">Optional short name of the assembly containing the type. For example 'System.Xml.ReaderWriter.dll'</param>
        /// <returns></returns>
        public override Type GetRemoteType(string typeFullName, string assembly = null)
        {
            // Easy case: Trying to resolve from cache or from local assemblies
            var resolver = TypesResolver.Instance;
            Type res = resolver.Resolve(assembly, typeFullName);
            if (res != null)
            {
                // Either found in cache or found locally.

                // If it's a local type we need to wrap it in a "fake" RemoteType (So method invocations will actually 
                // happend in the remote app, for example)
                // (But not for primitives...)
                if (!(res is RemoteType) && !res.IsPrimitive)
                {
                    res = new RemoteType(this, res);
                    // TODO: Registring here in the cache is a hack but we couldn't register within "TypesResolver.Resolve"
                    // because we don't have the ManagedRemoteApp to associate the fake remote type with.
                    // Maybe this should move somewhere else...
                    resolver.RegisterType(res);
                }

                return res;
            }

            // Harder case: Dump the remote type. This takes much more time (includes dumping of depedent
            // types) and should be avoided as much as possible.
            RemoteTypesFactory rtf =
                new RemoteTypesFactory(resolver, _managedCommunicator, avoidGenericsRecursion: true);
            var dumpedType = _managedCommunicator.DumpType(typeFullName, assembly);
            return rtf.Create(this, dumpedType);
        }

        /// <summary>
        /// Get a managed remote Enum
        /// </summary>
        public RemoteEnum GetRemoteEnum(string typeFullName, string assembly = null)
        {
            RemoteType remoteType = GetRemoteType(typeFullName, assembly) as RemoteType;
            if (remoteType == null)
            {
                throw new Exception("Failed to dump remote enum (and get a RemoteType object)");
            }
            return new RemoteEnum(remoteType);
        }

        //
        // Getting Remote Objects
        //

        public override ManagedRemoteObject GetRemoteObject(ulong remoteAddress, string typeName, int? hashCode = null)
        {
            return _remoteObjects.GetRemoteObject(remoteAddress, typeName, hashCode);
        }

        //
        // Inject assemblies
        //

        /// <summary>
        /// Loads an assembly into the remote process
        /// </summary>
        public bool InjectAssembly(Assembly assembly) => InjectAssembly(assembly.Location);

        /// <summary>
        /// Loads an assembly into the remote process
        /// </summary>
        public override bool InjectAssembly(string path)
        {
            bool res = _managedCommunicator.InjectAssembly(path);
            if (res)
            {
                // Re-setting the cached domains because otherwise we won't
                // see our newly injected module
                _managedDomains = null;
            }
            return res;
        }
        public override bool InjectDll(string path)
        {
            bool res = _managedCommunicator.InjectDll(path);
            if (res)
            {
                _managedDomains = null;
            }
            return res;
        }

        //
        // IDisposable
        //
        public override void Dispose()
        {
            _managedCommunicator?.KillDiver();
            _managedCommunicator = null;
            _procWithDiver = null;
        }

    }
}