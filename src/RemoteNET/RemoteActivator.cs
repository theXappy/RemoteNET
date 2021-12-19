using System;
using RemoteNET.Internal;
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
            ObjectOrRemoteAddress[] remoteParams = new ObjectOrRemoteAddress[parameters.Length];
            for (int i = 0; i < parameters.Length; i++)
            {
                object parameter = parameters[i];
                if (parameter.GetType().IsPrimitiveEtc() || parameter.GetType().IsEnum)
                {
                    remoteParams[i] = ObjectOrRemoteAddress.FromObj(parameter);
                }
                else if (parameter is RemoteObject remoteArg)
                {
                    remoteParams[i] = ObjectOrRemoteAddress.FromToken(remoteArg.RemoteToken, remoteArg.GetType().FullName);
                }
                else if (parameter is DynamicRemoteObject dro)
                {
                    RemoteObject originRemoteObject = dro.__ro;
                    remoteParams[i] = ObjectOrRemoteAddress.FromToken(originRemoteObject.RemoteToken, originRemoteObject.GetType().FullName);
                }
                else
                {
                    throw new Exception($"{nameof(RemoteActivator)}.{nameof(CreateInstance)} only works with primitive (int, " +
                                        $"double, string,...) or remote (in {nameof(RemoteObject)}) parameters. " +
                                        $"The parameter at index {i} was of unsupported type {parameters.GetType()}. \n" +
                                        $"If you are trying to pass a local object to a remote c'tor you need to construct " +
                                        $"that object in the remote application instead.");
                }
            }

            // Create object + pin
            InvocationResults invoRes = _communicator.CreateObject(t.FullName, remoteParams);

            // Get proxy object
            var remoteObject = _app.GetRemoteObject(invoRes.ReturnedObjectOrAddress.RemoteAddress);
            return remoteObject;
        }
    }
}
