using System;
using System.Collections.Generic;

namespace RemoteNET.Internal
{
    public class ProxiedMethodOverload
    {
        public Type ReturnType { get; set; }
        public List<Tuple<Type,string>> Parameters { get; set; }
        public Func<object[], object> Proxy { get; set; }
    }
}
