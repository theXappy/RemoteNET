using System.Collections.Generic;

namespace ScubaDiver.API.Interactions.Callbacks
{
    public class CallbackInvocationRequest
    {
        public string StackTrace { get; set; }
        public int Token { get; set; }
        public int ThreadID { get; set; }
        // Only valid for Postfix hooks on non-void functions.
        public ObjectOrRemoteAddress RetValue { get; set; }
        public List<ObjectOrRemoteAddress> Parameters { get; set; }

        public CallbackInvocationRequest()
        {
            Parameters = new();
        }
    }

}