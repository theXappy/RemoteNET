using System;
using System.Reflection;

namespace ScubaDiver
{
    public interface IProxyFunctionHolder
    {
        public Delegate RegisteredProxy { get;  }
    }

    public class RegisteredEventHandlerInfo : IProxyFunctionHolder
    {
        public Delegate RegisteredProxy { get; set; }
        // Note that this object might be pinned or unpinned when this info object is created
        // but by holding a reference to it within the class we don't care if it moves or
        // not - we will always be able to safely access it
        public object Target { get; set; }
        public EventInfo EventInfo { get; set; }
    }

    public class RegisteredMethodHookInfo : IProxyFunctionHolder
    {
        public Delegate RegisteredProxy { get; set; }
        public MethodInfo OriginalHookedMethod { get; set; }

    }
}