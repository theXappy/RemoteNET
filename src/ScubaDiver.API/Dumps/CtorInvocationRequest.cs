using System.Collections.Generic;

namespace ScubaDiver.API.Dumps
{
    public class CtorInvocationRequest
    {
        public string TypeFullName { get; set; }
        public List<ObjectOrRemoteAddress> Parameters { get; set; }

        public CtorInvocationRequest()
        {
            Parameters = new();
        }
    }
}