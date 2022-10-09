using System;

namespace ScubaDiver.API.Exceptions
{
    /// <summary>
    /// Encapsulates an exception that was thrown in the remote object and catched by the Diver.
    /// </summary>
    public class RemoteException : Exception
    {
        public string RemoteMessage { get; private set; }
        private string _remoteStackTrace;
        public string RemoteStackTrace => _remoteStackTrace;
        public override string StackTrace =>
            $"{_remoteStackTrace}\n" +
            $"--- End of remote exception stack trace ---\n" +
            $"{base.StackTrace}";

        public RemoteException(string msg, string remoteStackTrace)
        {
            RemoteMessage = msg;
            _remoteStackTrace = remoteStackTrace;
        }

        public override string ToString()
        {
            return RemoteMessage;
        }
    }
}
