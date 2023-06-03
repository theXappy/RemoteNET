using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using Reko.Core;
using RemoteNET.Internal;
using RemoteNET.RttiReflection;
using ScubaDiver.API;
using ScubaDiver.API.Interactions.Dumps;
using ScubaDiver.API.Utils;

namespace RemoteNET
{
    public class UnmanagedRemoteObject : IRemoteObject
    {
        private static int NextIndex = 1;
        public int Index;

        private readonly RemoteApp _app;
        private RemoteObjectRef _ref;
        private Type _type = null;

        private readonly Dictionary<Delegate, DiverCommunicator.LocalEventCallback> _eventCallbacksAndProxies;

        public ulong RemoteToken => _ref.Token;

        internal UnmanagedRemoteObject(RemoteObjectRef reference, RemoteApp remoteApp)
        {
            Index = NextIndex++;
            _app = remoteApp;
            _ref = reference;
            _eventCallbacksAndProxies = new Dictionary<Delegate, DiverCommunicator.LocalEventCallback>();
        }

        public Type GetRemoteType()
        {
            return _type ??= _app.GetRemoteType(_ref.GetTypeDump());
        }

        public dynamic Dynamify()
        {
            // Adding fields 
            ManagedTypeDump managedTypeDump = _ref.GetTypeDump();

            var factory = new DynamicRemoteObjectFactory();
            return factory.Create(_app, this, managedTypeDump);
        }

        public ObjectOrRemoteAddress GetItem(ObjectOrRemoteAddress key)
        {
            throw new NotImplementedException();
        }
    }


    public class UnmanagedRemoteApp : RemoteApp
    {
        private Process _procWithDiver;
        public Process Process => _procWithDiver;

        public RemoteActivator Activator => throw new NotImplementedException("Not yet");

        private DiverCommunicator _unmanagedCommunicator;
        public override DiverCommunicator Communicator => _unmanagedCommunicator;

        private List<string> _unmanagedModulesList;

        public UnmanagedRemoteApp(Process procWithDiver, DiverCommunicator unmanagedCommunicator)
        {
            _procWithDiver = procWithDiver;
            _unmanagedCommunicator = unmanagedCommunicator;
            _unmanagedCommunicator = unmanagedCommunicator;
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

            if (_unmanagedCommunicator != null)
            {
                _unmanagedModulesList ??=
                    _unmanagedCommunicator.DumpDomains().AvailableDomains.Single().AvailableModules;
                foreach (string module in _unmanagedModulesList)
                {
                    List<TypesDump.TypeIdentifiers> typeIdentifiers;
                    try
                    {
                        typeIdentifiers = _unmanagedCommunicator.DumpTypes(module).Types;
                    }
                    catch
                    {
                        // TODO:
                        Debug.WriteLine(
                            $"[{nameof(ManagedRemoteApp)}][{nameof(QueryTypes)}] Exception thrown when Dumping/Iterating unmanaged module: {module}");
                        continue;
                    }

                    foreach (TypesDump.TypeIdentifiers type in typeIdentifiers)
                    {
                        // TODO: Filtering should probably be done in the Diver's side
                        if (matchesFilter(type.TypeName))
                            yield return new CandidateType(RuntimeType.Unmanaged, type.TypeName, module);
                    }
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
            var managedHeapDump = Communicator.DumpHeap(typeFullNameFilter, dumpHashcodes);
            var managedCandidates = managedHeapDump.Objects.Select(heapObj => new CandidateObject(RuntimeType.Unmanaged, heapObj.Address, heapObj.Type, heapObj.HashCode));
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
            var resolver = RttiTypesResolver.Instance;
            Type res = resolver.Resolve(assembly, typeFullName);
            if (res != null)
            {
                // Found in cache.
                return res;
            }

            // Harder case: Dump the remote type. This takes much more time (includes dumping of depedent
            // types) and should be avoided as much as possible.
            RttiTypesFactory rtf =
                new RttiTypesFactory(resolver, _unmanagedCommunicator);
            var dumpedType = _unmanagedCommunicator.DumpType(typeFullName, assembly);
            return rtf.Create(this, dumpedType);
        }

        //
        // Getting Remote Objects
        //

        public override UnmanagedRemoteObject GetRemoteObject(ulong remoteAddress, string typeName, int? hashCode = null)
        {
            ObjectDump od;
            ManagedTypeDump td;
            try
            {
                od = _unmanagedCommunicator.DumpObject(remoteAddress, typeName, true, hashCode);
                td = _unmanagedCommunicator.DumpType(od.Type);
            }
            catch (Exception e)
            {
                throw new Exception("Could not dump remote object/type.", e);
            }


            var remoteObject = new UnmanagedRemoteObject(new RemoteObjectRef(od, td, _unmanagedCommunicator), this);
            return remoteObject;
        }

        //
        // Inject assemblies
        //

        /// <summary>
        /// Loads an assembly into the remote process
        /// </summary>
        public override bool InjectAssembly(string path)
        {
            bool res = _unmanagedCommunicator.InjectAssembly(path);
            if (res)
            {
                // Re-setting the cached modules list because otherwise we won't
                // see our newly injected module
                _unmanagedModulesList = null;
            }
            return res;
        }

        public override bool InjectDll(string path)
        {
            bool res = _unmanagedCommunicator.InjectDll(path);
            if (res)
            {
                // Re-setting the cached modules list because otherwise we won't
                // see our newly injected module
                _unmanagedModulesList = null;
            }
            return res;
        }

        //
        // IDisposable
        //
        public override void Dispose()
        {
            _unmanagedCommunicator?.KillDiver();
            _unmanagedCommunicator = null;
            _procWithDiver = null;
        }

    }
}