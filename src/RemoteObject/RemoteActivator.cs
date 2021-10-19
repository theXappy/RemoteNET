using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using RemoteObject.Internal;
using ScubaDiver;
using ScubaDiver.API;
using ScubaDiver.Extensions;

namespace RemoteObject
{
    public class RemoteActivator
    {
        private DiverCommunicator _communicator;

        internal RemoteActivator(DiverCommunicator communicator)
        {
            _communicator = communicator;
        }

        public RemoteObject CreateInstance(Type t, params object[] parameters)
        {
            ObjectOrRemoteAddress[] remoteParams = new ObjectOrRemoteAddress[parameters.Length];
            for (int i = 0; i < parameters.Length; i++)
            {
                object parameter = parameters[i];
                if (parameter.GetType().IsPrimitiveEtc())
                {
                    remoteParams[i] = ObjectOrRemoteAddress.FromObj(parameter);
                }
                else if (parameter is RemoteObject remoteArg)
                {
                    remoteParams[i] = ObjectOrRemoteAddress.FromToken(remoteArg.RemoteToken, remoteArg.GetType().FullName);
                }
                else
                {
                    throw new Exception($"{nameof(RemoteActivator)}.{nameof(CreateInstance)} only works with primitive (int, " +
                                        $"double, string,...) or remote (in {nameof(RemoteObject)}) parameters. " +
                                        $"The parameter at index {i} was of unsupported type {parameters.GetType()}");
                }
            }

            ObjectDump od;
            TypeDump td;
            try
            {
                od = _communicator.CreateObject(t.FullName, remoteParams);
                td = _communicator.DumpType(od.Type);
            }
            catch (Exception e)
            {
                throw new Exception("Could not dump remote object/type.", e);
            }

            var remoteObject = new RemoteObject(new RemoteObjectRef(od, td, _communicator));
            return remoteObject;
        }
    }
}
