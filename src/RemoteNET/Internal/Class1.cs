using System;
using System.Collections.Generic;
using System.Text;

namespace RemoteNET.Internal
{
    public interface IDynamicRemoteObject
    {
        public RemoteObject __ro { get; }
    }
}
