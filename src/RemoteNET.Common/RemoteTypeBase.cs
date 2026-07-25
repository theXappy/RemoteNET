using System;

namespace RemoteNET.Common
{
    public abstract class RemoteTypeBase : Type
    {
        public Func<object, string> ToStringHook { get; set; }
    }
}
