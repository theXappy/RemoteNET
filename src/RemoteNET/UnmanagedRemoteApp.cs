using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using RemoteNET.Common;
using RemoteNET.Internal;
using RemoteNET.RttiReflection;
using ScubaDiver.API;
using ScubaDiver.API.Interactions;
using ScubaDiver.API.Interactions.Dumps;
using ScubaDiver.API.Utils;

namespace RemoteNET
{
    public class UnmanagedRemoteApp : RemoteApp
    {
        private Action<string> _logger = (str) => { };// Console.WriteLine($"[{DateTime.Now.ToLongTimeString()}]{str}");

        private Process _procWithDiver;
        public Process Process => _procWithDiver;

        RemoteActivator _activator;
        public override RemoteActivator Activator => _activator;

        private DiverCommunicator _unmanagedCommunicator;
        private readonly RemoteAppsHub _hub;
        public override DiverCommunicator Communicator => _unmanagedCommunicator;
        private RemoteHookingManager _hookingManager;
        public override RemoteHookingManager HookingManager => _hookingManager;
        public override RemoteMarshal Marshal { get; }


        public UnmanagedRemoteApp(Process procWithDiver, DiverCommunicator unmanagedCommunicator, RemoteAppsHub hub)
        {
            _procWithDiver = procWithDiver;
            _unmanagedCommunicator = unmanagedCommunicator;
            _hub = hub;
            _hookingManager = new RemoteHookingManager(this);
            if (hub.TryGetValue(RuntimeType.Managed, out RemoteApp managedApp) && managedApp is ManagedRemoteApp castedManagedApp)
                Marshal = new RemoteMarshal(castedManagedApp);
            _activator = new UnmanagedRemoteActivator(this);
        }

        //
        // Init
        // 

        //
        // Remote Heap querying
        //

        public override IEnumerable<CandidateType> QueryTypes(string typeFullNameFilter)
            => QueryTypes(typeFullNameFilter, null);

        public IEnumerable<CandidateType> QueryTypes(string typeFullNameFilter, string importerModule)
        {
            _logger($"[QueryTypes] Enter with filter {typeFullNameFilter}");
            TypesDump resutls = _unmanagedCommunicator.DumpTypes(typeFullNameFilter, importerModule);

            foreach (TypesDump.TypeIdentifiers type in resutls.Types)
            {
                ulong? xoredMethodTable = null;
                if (type.XoredMethodTable.HasValue)
                    xoredMethodTable = type.XoredMethodTable ^ TypesDump.TypeIdentifiers.XorMask;
                yield return new CandidateType(RuntimeType.Unmanaged, type.FullTypeName, type.Assembly, xoredMethodTable);
            }
            _logger($"[QueryTypes] Enter with filter {typeFullNameFilter} -- DONE");
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
            _logger($"[GetRemoteType] Enter with filter = {typeFullName}, Assembly = {assembly ?? "null"}");

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
            Type results = rtf.Create(this, dumpedType);
            if (results != null)
                resolver.RegisterType(assembly, typeFullName, results);

            _logger($"[GetRemoteType] Enter with filter = {typeFullName}, Assembly = {assembly ?? "null"} -- DONE");
            return results;
        }

        /// <summary>
        /// Gets a handle to a remote type
        /// </summary>
        /// <param name="methodTableAddress">Method Table Address to look for</param>
        /// <returns></returns>
        public override Type GetRemoteType(long methodTableAddress)
        {
            _logger($"[GetRemoteType] Enter with Method Table = {methodTableAddress:X16}");


            // Easy case: Trying to resolve from cache or from local assemblies
            var resolver = RttiTypesResolver.Instance;
            Type res = resolver.Resolve(methodTableAddress);
            if (res != null)
            {
                // Found in cache.
                _logger($"[GetRemoteType] Enter with Method Table = {methodTableAddress:X16} -- DONE, WAS IN CACHE");
                return res;
            }

            // Harder case: Dump the remote type. This takes much more time (includes dumping of depedent
            // types) and should be avoided as much as possible.
            RttiTypesFactory rtf =
                new RttiTypesFactory(resolver, _unmanagedCommunicator);
            var dumpedType = _unmanagedCommunicator.DumpType(methodTableAddress);
            var results = rtf.Create(this, dumpedType);
            _logger($"[GetRemoteType] Enter with Method Table = {methodTableAddress:X16} -- DONE, WAS NOT in CACHE");
            return results;
        }

        //
        // Getting Remote Objects
        //

        public override UnmanagedRemoteObject GetRemoteObject(ulong remoteAddress, string typeName, int? hashCode = null)
        {
            _logger($"[GetRemoteObject] Enter with Address = {remoteAddress:X16}, TypeName = {typeName}");

            ObjectDump od;
            TypeDump td;
            try
            {
                od = _unmanagedCommunicator.DumpObject(remoteAddress, typeName, true, hashCode);
                string namespaceAndType = od.Type;
                string assembly = null;
                if (namespaceAndType.Contains("!"))
                {
                    var split = namespaceAndType.Split('!');
                    assembly = split[0];
                    namespaceAndType = split[1].Trim();
                }
                td = _unmanagedCommunicator.DumpType(namespaceAndType, assembly);
            }
            catch (Exception e)
            {
                throw new AggregateException("Could not dump remote object/type.", e);
            }


            var remoteObject = new UnmanagedRemoteObject(new RemoteObjectRef(od, td, _unmanagedCommunicator), this);
            _logger($"[GetRemoteObject] Enter with Address = {remoteAddress:X16}, TypeName = {typeName} -- DONE");
            return remoteObject;
        }

        public override RemoteObject GetRemoteObject(ObjectOrRemoteAddress oora)
        {
            _logger($"[GetRemoteObject] Enter with OORA");


            if (oora.Type == typeof(CharStar).FullName)
            {
                _logger($"[GetRemoteObject] Enter with OORA -- CharStar shortcut");
                return new RemoteCharStar(_hub[RuntimeType.Managed] as ManagedRemoteApp, oora.RemoteAddress, oora.EncodedObject);
            }

            UnmanagedRemoteObject results = GetRemoteObject(oora.RemoteAddress, oora.Type);
            _logger($"[GetRemoteObject] Enter with OORA -- DONE");
            return results;
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
            return res;
        }

        public override bool InjectDll(string path)
        {
            bool res = _unmanagedCommunicator.InjectDll(path);
            return res;
        }

        //
        // Custom Functions
        //

        /// <summary>
        /// Registers a custom function on a remote type for unmanaged targets
        /// </summary>
        /// <param name="parentType">The type to add the function to</param>
        /// <param name="functionName">Name of the function</param>
        /// <param name="moduleName">Module name where the function is located (e.g., "MyModule.dll")</param>
        /// <param name="offset">Offset within the module where the function is located</param>
        /// <param name="returnType">Return type of the function</param>
        /// <param name="parameterTypes">Parameter types of the function</param>
        /// <returns>True if registration was successful, false otherwise</returns>
        public bool RegisterCustomFunction(
            Type parentType,
            string functionName,
            string moduleName,
            ulong offset,
            Type returnType,
            params Type[] parameterTypes)
        {
            if (parentType == null)
                throw new ArgumentNullException(nameof(parentType));
            if (string.IsNullOrEmpty(functionName))
                throw new ArgumentException("Function name cannot be null or empty", nameof(functionName));
            if (string.IsNullOrEmpty(moduleName))
                throw new ArgumentException("Module name cannot be null or empty", nameof(moduleName));

            var request = new RegisterCustomFunctionRequest
            {
                ParentTypeFullName = parentType.FullName,
                ParentAssembly = parentType.Assembly?.GetName()?.Name,
                FunctionName = functionName,
                ModuleName = moduleName,
                Offset = offset,
                ReturnTypeFullName = returnType?.FullName ?? "void",
                ReturnTypeAssembly = returnType?.Assembly?.GetName()?.Name,
                Parameters = parameterTypes?.Select((pt, idx) => new RegisterCustomFunctionRequest.ParameterTypeInfo
                {
                    Name = $"param{idx}",
                    TypeFullName = pt.FullName,
                    Assembly = pt.Assembly?.GetName()?.Name
                }).ToList() ?? new List<RegisterCustomFunctionRequest.ParameterTypeInfo>()
            };

            return _unmanagedCommunicator.RegisterCustomFunction(request);
        }

        //
        // IDisposable
        //
        public override void Dispose()
        {
            try { _unmanagedCommunicator?.KillDiver(); } catch { }
            try { _unmanagedCommunicator?.Dispose(); } catch { }
            _unmanagedCommunicator = null;
            _procWithDiver = null;
        }

    }
}