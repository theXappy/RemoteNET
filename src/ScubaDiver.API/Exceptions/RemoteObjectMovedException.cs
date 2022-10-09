using System;

namespace ScubaDiver.API.Exceptions
{
    internal class RemoteObjectMovedException : Exception
    {
        public ulong TestedAddress { get; private set; }
        public RemoteObjectMovedException(ulong testedAddress, string msg) : base(msg) 
        {
            TestedAddress = testedAddress;
        }
    }
}
