using System.Collections.Generic;

namespace ScubaDiver.API.Dumps
{
    public class TypeDumpRequest
    {
        public string Assembly { get; set; }
        public string TypeFullName { get; set; }

    }

    public class InvocationRequest
    {
        public ulong ObjAddress { get; set; }
        public string MethodName { get; set; }
        public string TypeFullName { get; set; }
        public string[] GenericArgsTypeFullNames { get; set; }
        public List<ObjectOrRemoteAddress> Parameters { get; set; }

        public InvocationRequest()
        {
            GenericArgsTypeFullNames = new string[0];
            Parameters = new();
        }
    }
    
}