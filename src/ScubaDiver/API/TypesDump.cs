using System.Collections.Generic;

namespace ScubaDiver.API
{
    public class TypesDump
    {
        public class TypeIdentifiers
        {
            public string TypeName { get; set; }
            public ulong MethodTable { get; set; }
            public int Token { get; set; }
        }
        public string AssemblyName { get; set; }
        public List<TypeIdentifiers> Types { get; set; }
    }
}