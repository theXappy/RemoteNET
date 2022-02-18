using System.Dynamic;
using ScubaDiver.API;

namespace RemoteNET.Internal.Reflection
{
    public class DynamicRemoteEnum : DynamicObject
    {
        private readonly RemoteEnum _remoteEnum;
        public RemoteApp App => _remoteEnum.App;

        public DynamicRemoteEnum(RemoteEnum remoteEnum)
        {
            _remoteEnum = remoteEnum;
        }

        public override bool TryGetMember(GetMemberBinder binder, out object result)
        {
            string memberName = binder.Name;
            ObjectOrRemoteAddress oora = _remoteEnum.GetValue(memberName);
            RemoteObject ro = App.GetRemoteObject(oora.RemoteAddress);
            result = ro.Dynamify();
            return true;
        }

    }
}