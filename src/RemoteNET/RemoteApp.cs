using System;
using System.Collections.Generic;
using ScubaDiver.API;
using ScubaDiver.API.Interactions.Dumps;

namespace RemoteNET;

public abstract class RemoteApp : IDisposable
{
    public abstract DiverCommunicator Communicator { get; }
    public abstract RemoteHookingManager HookingManager { get; }
    public abstract RemoteMarshal Marshal { get; }

    public abstract IEnumerable<CandidateType> QueryTypes(string typeFullNameFilter);

    /// <summary>
    /// Gets all object candidates for a specific filter
    /// </summary>
    /// <param name="typeFullNameFilter">Objects with Full Type Names of this EXACT string will be returned. You can use '*' as a "0 or more characters" wildcard</param>
    /// <param name="dumpHashcodes">Whether to also dump hashcodes of every matching object.
    /// This makes resolving the candidates later more reliable but for wide queries (e.g. "*") this might fail the entire search since it causes instabilities in the heap when examining it.
    /// </param>
    public abstract IEnumerable<CandidateObject> QueryInstances(string typeFullNameFilter, bool dumpHashcodes = true);
    public virtual IEnumerable<CandidateObject> QueryInstances(CandidateType typeFilter, bool dumpHashcodes = true) => QueryInstances(typeFilter.TypeFullName, dumpHashcodes);
    public virtual IEnumerable<CandidateObject> QueryInstances(Type typeFilter, bool dumpHashcodes = true) => QueryInstances(typeFilter.FullName, dumpHashcodes);

    /// <summary>
    /// Gets a handle to a remote type (even ones from assemblies we aren't referencing/loading to the local process)
    /// </summary>
    /// <param name="typeFullName">Full name of the type to get. For example 'System.Xml.XmlDocument'</param>
    /// <param name="assembly">Optional short name of the assembly containing the type. For example 'System.Xml.ReaderWriter.dll'</param>
    /// <returns></returns>
    public abstract Type GetRemoteType(string typeFullName, string assembly = null);

    /// <summary>
    /// Returns a handle to a remote type based on a given local type.
    /// </summary>
    public virtual Type GetRemoteType(Type localType) => GetRemoteType(localType.FullName, localType.Assembly.GetName().Name);
    public virtual Type GetRemoteType(CandidateType candidate) => GetRemoteType(candidate.TypeFullName, candidate.Assembly);
    public virtual Type GetRemoteType(TypeDump typeDump) => GetRemoteType(typeDump.Type, typeDump.Assembly);


    public abstract RemoteObject GetRemoteObject(ulong remoteAddress, string typeName, int? hashCode = null);
    public virtual RemoteObject GetRemoteObject(ObjectOrRemoteAddress oora) => GetRemoteObject(oora.RemoteAddress, oora.Type);
    public virtual RemoteObject GetRemoteObject(CandidateObject candidate) => GetRemoteObject(candidate.Address, candidate.TypeFullName, candidate.HashCode);

    /// <summary>
    /// Loads an assembly into the remote process
    /// </summary>
    public abstract bool InjectAssembly(string path);

    public abstract bool InjectDll(string path);

    public abstract void Dispose();
}