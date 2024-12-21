using System;
using System.Linq;
using RemoteNET.Internal.Reflection.DotNet;
using ScubaDiver.API;
using ScubaDiver.API.Interactions;

namespace RemoteNET
{
    public class ManagedRemoteActivator : RemoteActivator
    {
        private readonly ManagedRemoteApp _app;
        private readonly DiverCommunicator _communicator;

        internal ManagedRemoteActivator(DiverCommunicator communicator, ManagedRemoteApp app)
        {
            _communicator = communicator;
            _app = app;
        }


        public override ManagedRemoteObject CreateInstance(string assembly, string typeFullName, params object[] parameters)
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

            ObjectOrRemoteAddress[] remoteParams = paramsNoEnums.Select(ManagedRemoteFunctionsInvokeHelper.CreateRemoteParameter).ToArray();

            // Create object + pin
            InvocationResults invoRes = _communicator.CreateObject(typeFullName, remoteParams);

            // Get proxy object
            var remoteObject = _app.GetRemoteObject(invoRes.ReturnedObjectOrAddress) as ManagedRemoteObject;
            return remoteObject;
        }
    }
}
