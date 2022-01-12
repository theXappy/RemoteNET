using System.Collections.Generic;

namespace ScubaDiver.API.Dumps
{
    public class FunctionHookRequest
    {
        public string MethodName { get; set; }
        public string TypeFullName { get; set; }
        public List<string> ParametersTypeFullNames { get; set; }

        public string HookPosition { get; set; } // FFS: "Pre" or "Post"

    }
    
}