using System.Collections.Generic;

namespace ScubaDiver.API
{
    public class InvocationResults
    {
        public bool VoidReturnType { get; set; }
        public ObjectOrRemoteAddress ReturnedObjectOrAddress { get; set; }
    }
    public class InvocationArgument
    {
        /// <summary>
        /// TypeFullName of the argument in the destination program
        /// </summary>
        public string Type { get; set; }
        /// <summary>
        /// In case the type is a primitive and its value can be encoded
        /// </summary>
        public string EncodedValue { get; set; }
        /// <summary>
        /// In case a pinned object should be used for this parameter
        /// </summary>
        public ulong PinnedObjectAddress { get; set; }
    }

    public class CtorInvocationRequest
    {
        public string TypeFullName { get; set; }
        public List<InvocationArgument> Parameters { get; set; }

        public CtorInvocationRequest()
        {
            Parameters = new();
        }
    }

    public class InvocationRequest
    {
        public ulong ObjAddress { get; set; }
        public string MethodName { get; set; }
        public List<InvocationArgument> Parameters { get; set; }

        public InvocationRequest()
        {
            Parameters = new();
        }
    }
}