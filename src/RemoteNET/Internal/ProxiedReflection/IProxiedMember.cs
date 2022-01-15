using System;
using System.Collections.Generic;
using System.Text;

namespace RemoteNET.Internal.ProxiedReflection
{
    public interface IProxiedMember
    {
        public ProxiedMemberType Type { get; }
    }
}
