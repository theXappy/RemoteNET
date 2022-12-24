using System.Collections.Generic;

namespace ScubaDiver.API.Interactions.Dumps
{
    public class TypesDump
    {
        public class TypeIdentifiers
        {
            public string TypeName { get; set; }
        }
        public string AssemblyName { get; set; }
        public List<TypeIdentifiers> Types { get; set; }
    }
}