using System.Collections.Generic;
using System.Diagnostics.Contracts;

namespace ScubaDiver.API.Interactions.Callbacks
{
    public class FunctionHookRequest
    {
        public string IP { get; set; }
        public int Port { get; set; }
        public string MethodName { get; set; }
        public string TypeFullName { get; set; }
        public List<string> ParametersTypeFullNames { get; set; }

        public string HookPosition { get; set; } // FFS: "Pre" or "Post"

    }

}