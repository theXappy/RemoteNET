using System;
using System.Reflection;
using ScubaDiver.API;

namespace RemoteNET.Internal.Reflection
{
    public class RemoteEnum
    {
        private Type _remoteType;
        public RemoteApp App => (_remoteType as RemoteType)?.App;

        public RemoteEnum(Type remoteType)
        {
            _remoteType = remoteType;
        }

        public ObjectOrRemoteAddress GetValue(string valueName)
        {
            FieldInfo verboseField = _remoteType.GetField(valueName);
            ObjectOrRemoteAddress logginVerboseOora = (ObjectOrRemoteAddress)verboseField.GetValue(null);
            return logginVerboseOora;
        }

        public dynamic Dynamify()
        {
            return new DynamicRemoteEnum(this);
        }
    }
}