using System.Collections.Generic;

namespace ScubaDiver.API.Dumps
{
    public class CallbackInvocationRequest
    {
        public string StackTrace { get; set; }
        public int Token { get; set; }
        public List<ObjectOrRemoteAddress> Parameters { get; set; }

        public CallbackInvocationRequest()
        {
            Parameters = new();
        }
    }
    
}