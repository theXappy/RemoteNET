using System;
using System.Collections.Generic;
using RemoteNET.Internal.Reflection;

namespace RemoteNET.Internal.ProxiedReflection
{
    public class ProxiedMethodOverload
    {
        public Type ReturnType { get; set; }
        public List<RemoteParameterInfo> Parameters { get; set; }
        public Func<object[], object> Proxy
        {
            get
            {
                return (object[] arr) => GenericProxy(null, arr);
            }
        }
        public Func<Type[], object[], object> GenericProxy { get; set; }
        public List<string> GenericArgs { get; set; }
        public bool IsGenericMethod => GenericArgs?.Count > 0;
    }
}
