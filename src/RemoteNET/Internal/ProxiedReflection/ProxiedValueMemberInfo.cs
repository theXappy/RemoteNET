using System;

namespace RemoteNET.Internal.ProxiedReflection
{
    /// <summary>
    /// Info of proxied field or property
    /// </summary>
    public class ProxiedValueMemberInfo : IProxiedMember
    {
        public string FullTypeName { get; set; }
        public Action<object> Setter { get; set; }
        public Func<object> Getter { get; set; }

        public ProxiedMemberType Type { get; set; }
        public ProxiedValueMemberInfo(ProxiedMemberType type)
        {
            Type = type;
        }
    }
}
