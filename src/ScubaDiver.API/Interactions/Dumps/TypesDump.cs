using System.Collections.Generic;

namespace ScubaDiver.API.Interactions.Dumps
{
    public class TypesDump
    {
        public class TypeIdentifiers
        {
            public string Assembly { get; set; }
            public string FullTypeName { get; set; }
            public ulong? MethodTable { get; set; }
            public TypeIdentifiers(string assembly, string fullTypeName, ulong? methodTable = null)
            {
                this.Assembly = assembly;
                this.FullTypeName = fullTypeName;
                this.MethodTable = methodTable;
            }
        }
        public List<TypeIdentifiers> Types { get; set; }
    }
}