using System.Collections.Generic;

namespace ScubaDiver.API
{
    public class InvocationResults
    {
        public bool VoidReturnType { get; set; }
        public ObjectOrRemoteAddress ReturnedObjectOrAddress { get; set; }
    }

    public class CtorInvocationRequest
    {
        public string TypeFullName { get; set; }
        public List<ObjectOrRemoteAddress> Parameters { get; set; }

        public CtorInvocationRequest()
        {
            Parameters = new();
        }
    }

    public class InvocationRequest
    {
        public ulong ObjAddress { get; set; }
        public string MethodName { get; set; }
        public string TypeFullName { get; set; }
        public List<ObjectOrRemoteAddress> Parameters { get; set; }

        public InvocationRequest()
        {
            Parameters = new();
        }
    }

    public class FieldSetRequest
    {
        public ulong ObjAddress { get; set; }
        public string TypeFullName { get; set; }
        public string FieldName { get; set; }
        public ObjectOrRemoteAddress Value { get; set; }
    }

}