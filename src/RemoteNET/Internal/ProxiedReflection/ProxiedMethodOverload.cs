using System;
using System.Collections.Generic;

namespace RemoteNET.Internal
{
    public class ProxiedMethodOverload
    {
        public List<Type> ArgumentsTypes { get; set; }
        public Func<object[], object> Proxy { get; set; }
    }
}
