using System;
using System.Collections.Generic;
using System.Text;

namespace ScubaDiver.API
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
