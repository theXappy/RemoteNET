using System.Collections.Generic;

namespace ScubaDiver.API.Interactions.Dumps
{
    public class TypesDump
    {
        public class TypeIdentifiers
        {
            public string Assembly { get; set; }
            public string FullTypeName { get; set; }
        }
        public List<TypeIdentifiers> Types { get; set; }
    }
}