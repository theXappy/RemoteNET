using System;
using System.Collections.Generic;

namespace RemoteNET.Internal
{
    public class ProxiedMethodOverload
    {
        public Type ReturnType { get; set; }
        public List<Tuple<Type,string>> Parameters { get; set; }
        public Func<object[], object> Proxy
        {
            get
            {
                return (object[] arr) => GenericProxy(null, arr);
            }
        }
        public Func<Type[], object[], object> GenericProxy { get; set; }
        public int NumOfGenericParameters { get; set; }
        public bool IsGenericMethod => NumOfGenericParameters > 0;
    }
}
