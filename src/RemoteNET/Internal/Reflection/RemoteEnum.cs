using System;
using System.Reflection;
using ScubaDiver.API;

namespace RemoteNET.Internal.Reflection
{
    public class RemoteEnum
    {
        private readonly RemoteType _remoteType;
        public ManagedRemoteApp App => (_remoteType as RemoteType)?.App;

        public RemoteEnum(RemoteType remoteType)
        {
            _remoteType = remoteType;
        }

        public object GetValue(string valueName)
        {
            // NOTE: This is breaking the "RemoteX"/"DynamicX" paradigm because we are effectivly returning a DRO here
            // How did that happen?
            // Well, Unlike RemoteObject which uses directly a remote token + ManagedTypeDump to read/write fields/props/methods
            // RemoteEnum was created after RemoteType was defined and it felt much easier to utilize it.
            // RemoteType itself, as part of the reflection API, returns DROs when invoked.
            RemoteFieldInfo verboseField = _remoteType.GetField(valueName) as RemoteFieldInfo;
            return verboseField.GetValue(null);
        }

        public dynamic Dynamify()
        {
            return new DynamicRemoteEnum(this);
        }
    }
}