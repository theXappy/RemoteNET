using RemoteNET.Internal.Reflection;
using ScubaDiver.API;
using ScubaDiver.API.Interactions.Dumps;
using ScubaDiver.API.Utils;
using System;
using System.Collections.Generic;
using System.Linq;
using RemoteNET.RttiReflection;

namespace RemoteNET.Internal
{
    public class DynamicRemoteObjectFactory
    {
        public DynamicRemoteObject Create(RemoteApp rApp, RemoteObject remoteObj, ManagedTypeDump managedTypeDump)
        {
            if(rApp is ManagedRemoteApp)
                return new DynamicManagedRemoteObject(rApp, remoteObj);
            if(rApp is UnmanagedRemoteApp)
                return new DynamicUnmanagedRemoteObject(rApp, remoteObj);
            throw new NotImplementedException($"Unexpected RemoteApp subtype: {rApp?.GetType().Name}");
        }
    }
}
