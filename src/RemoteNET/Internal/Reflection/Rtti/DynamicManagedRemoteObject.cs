using System.Collections;
using System.Diagnostics;
using RemoteNET.Internal;
using ScubaDiver.API.Utils;

namespace RemoteNET.RttiReflection
{

    /// <summary>
    /// A proxy of a remote object.
    /// Usages of this class should be strictly as a `dynamic` variable.
    /// Field/Property reads/writes are redirect to reading/writing to the fields of the remote object
    /// Function calls are redirected to functions calls in the remote process on the remote object
    /// 
    /// </summary>
    [DebuggerDisplay("Dynamic Proxy of {" + nameof(__ro) + "} (Managed)")]

    public class DynamicUnmanagedRemoteObject : DynamicRemoteObject
    {
        public DynamicUnmanagedRemoteObject(RemoteApp ra, IRemoteObject ro) : base(ra, ro)
        {
        }
    }
}
