using System;

namespace ScubaDiver.API
{
    public class RemoteException : Exception
    {
        public string Message { get; private set; }
        public RemoteException(string msg)
        {
            Message = msg;
        }
    }
}
