using RemoteNET.Internal.Reflection;
using ScubaDiver.API;
using ScubaDiver.API.Interactions.Dumps;
using ScubaDiver.API.Utils;
using System;
using System.Collections.Generic;
using System.Linq;

namespace RemoteNET.Internal
{
    public class DynamicRemoteObjectFactory
    {
        private ManagedRemoteApp _app;

        public DynamicRemoteObject Create(ManagedRemoteApp rApp, RemoteObject remoteObj, ManagedTypeDump managedTypeDump)
        {
            _app = rApp;
            return new DynamicRemoteObject(rApp, remoteObj);
        }
    }
}
