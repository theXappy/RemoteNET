using System;
using System.Collections.Generic;
using System.Linq;
using RemoteNET.Internal.Reflection;
using ScubaDiver.API;
using ScubaDiver.API.Dumps;

namespace RemoteNET
{
    public class RemoteActivator
    {
        private readonly RemoteApp _app;
        private readonly DiverCommunicator _communicator;

        internal RemoteActivator(DiverCommunicator communicator, RemoteApp app)
        {
            _communicator = communicator;
            _app = app;
        }


        public RemoteObject CreateInstance(string typeFullName, params object[] parameters)
            => CreateInstance(_app.GetRemoteType(typeFullName), parameters);

        public RemoteObject CreateInstance(Type t, params object[] parameters)
        {
            object[] paramsNoEnums = parameters.ToArray();
            for (int i = 0; i < paramsNoEnums.Length; i++)
            {
                var val = paramsNoEnums[i];
                if (val.GetType().IsEnum)
                {
                    var enumClass = this._app.GetRemoteEnum(val.GetType().FullName);
                    // TODO: This will break on the first enum value which represents 2 or more flags
                    object enumVal = enumClass.GetValue(val.ToString());
                    // NOTE: Object stays in place in the remote app as long as we have it's reference
                    // in the paramsNoEnums array (so untill end of this method)
                    paramsNoEnums[i] = enumVal;
                }
            }


            ObjectOrRemoteAddress[] remoteParams = paramsNoEnums.Select(RemoteFunctionsInvokeHelper.CreateRemoteParameter).ToArray();

            // Create object + pin
            InvocationResults invoRes = _communicator.CreateObject(t.FullName, remoteParams);

            // Get proxy object
            var remoteObject = _app.GetRemoteObject(invoRes.ReturnedObjectOrAddress.RemoteAddress);
            return remoteObject;
        }
    }
}
