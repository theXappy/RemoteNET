using RemoteNET.Internal.ProxiedReflection;
using System.Collections.Generic;

namespace RemoteNET.Internal
{
    public class ProxiedMethodGroup : List<ProxiedMethodOverload>, IProxiedMember
    {
        public ProxiedMemberType Type => ProxiedMemberType.Method;
    }
}
