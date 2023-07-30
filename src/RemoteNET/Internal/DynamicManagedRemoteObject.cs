using Microsoft.CSharp.RuntimeBinder;
using RemoteNET.Internal.ProxiedReflection;
using RemoteNET.Utils;
using ScubaDiver.API.Utils;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Dynamic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using RemoteNET.Common;
using Binder = Microsoft.CSharp.RuntimeBinder.Binder;
using RemoteNET.Internal.Reflection.DotNet;

namespace RemoteNET.Internal
{

    /// <summary>
    /// A proxy of a remote object.
    /// Usages of this class should be strictly as a `dynamic` variable.
    /// Field/Property reads/writes are redirect to reading/writing to the fields of the remote object
    /// Function calls are redirected to functions calls in the remote process on the remote object
    /// 
    /// </summary>
    [DebuggerDisplay("Dynamic Proxy of {" + nameof(__ro) + "} (Managed)")]

    public class DynamicManagedRemoteObject : DynamicRemoteObject, IEnumerable
    {
        public DynamicManagedRemoteObject(RemoteApp ra, RemoteObject ro) : base(ra, ro)
        {
        }

        /// <summary>
        /// Array access. Key can be any primitive / RemoteObject / DynamicRemoteObject
        /// </summary>
        public dynamic this[object key]
        {
            get
            {
                ScubaDiver.API.ObjectOrRemoteAddress ooraKey = ManagedRemoteFunctionsInvokeHelper.CreateRemoteParameter(key);
                ScubaDiver.API.ObjectOrRemoteAddress item = __ro.GetItem(ooraKey);
                if (item.IsNull)
                {
                    return null;
                }
                else if (item.IsRemoteAddress)
                {
                    return __ra.GetRemoteObject(item.RemoteAddress, item.Type).Dynamify();
                }
                else
                {
                    return PrimitivesEncoder.Decode(item.EncodedObject, item.Type);
                }
            }
            set => throw new NotImplementedException();
        }

        public IEnumerator GetEnumerator()
        {
            if (!__members.Any(member => member.Name == nameof(GetEnumerator)))
                throw new Exception($"No method called {nameof(GetEnumerator)} found. The remote object probably doesn't implement IEnumerable");

            dynamic enumeratorDro = InvokeMethod<object>(nameof(GetEnumerator));
            return new DynamicRemoteEnumerator(enumeratorDro);
        }

    }
}
