using System;
using System.Linq;
using RemoteNET.Internal;
using RemoteNET.Internal.Reflection;
using ScubaDiver;
using ScubaDiver.API;
using ScubaDiver.API.Dumps;
using ScubaDiver.API.Extensions;

namespace RemoteNET
{
    public class RemoteActivator
    {
        private RemoteApp _app;
        private DiverCommunicator _communicator;

        internal RemoteActivator(DiverCommunicator communicator, RemoteApp app)
        {
            _communicator = communicator;
            _app = app;
        }


        public RemoteObject CreateInstance(string typeFullName, params object[] parameters)
            => CreateInstance(_app.GetRemoteType(typeFullName), parameters);

        public RemoteObject CreateInstance(Type t, params object[] parameters)
        {
            ObjectOrRemoteAddress[] remoteParams = parameters.Select(RemoteFunctionsInvokeHelper.CreateRemoteParameter).ToArray();

            // Create object + pin
            InvocationResults invoRes = _communicator.CreateObject(t.FullName, remoteParams);

            // Get proxy object
            var remoteObject = _app.GetRemoteObject(invoRes.ReturnedObjectOrAddress.RemoteAddress);
            return remoteObject;
        }
    }
}
