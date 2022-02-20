using System;
using System.Reflection;
using ScubaDiver.API;

namespace RemoteNET.Internal.Reflection
{
    public class RemoteEnum
    {
        private readonly Type _remoteType;
        public RemoteApp App => (_remoteType as RemoteType)?.App;

        public RemoteEnum(Type remoteType)
        {
            _remoteType = remoteType;
        }

        public dynamic GetValue(string valueName)
        {
            FieldInfo verboseField = _remoteType.GetField(valueName);
            return verboseField.GetValue(null);
        }

        public dynamic Dynamify()
        {
            return new DynamicRemoteEnum(this);
        }
    }
}