using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using RemoteObject.Internal;
using ScubaDiver;

namespace RemoteObject
{
    public class RemoteObjectsProvider
    {
        private readonly Process _procWithDiver;
        private readonly DiverCommunicator _communicator;

        private RemoteObjectsProvider(Process procWithDiver, DiverCommunicator communicator)
        {
            _procWithDiver = procWithDiver;
            _communicator = communicator;
        }

        public List<HeapDump.HeapObject> QueryRemoteInstances(string typeFilter)
        {
            return _communicator.DumpHeap(typeFilter).Objects;
        }

        public RemoteObject CreateRemoteObject(ulong remoteAddress)
        {
            ObjectDump od;
            TypeDump td;
            try
            {
                od = _communicator.DumpObject(remoteAddress, true);
                td = _communicator.DumpType(od.Type);
            }
            catch(Exception e)
            {
                throw new Exception("Could not dump remote object/type.", e);
            }

            var remoteObject = new RemoteObject(new RemoteObjectRef(od, td, _communicator));
            return remoteObject;
        }

        public static RemoteObjectsProvider Create(Process target)
        {
            // TODO: Get inject, bootstrap and ScubaDiver from resources...
            var injectorProc = Process.Start("Injector.exe", $"{target.Id}");
            // TODO: Get results of injector

            // TODO: Make it configurable
            string diverAddr = "127.0.0.1";
            int diverPort = 9977;
            DiverCommunicator com = new DiverCommunicator(diverAddr, diverPort);

            return new RemoteObjectsProvider(target, com);
        }

    }
}