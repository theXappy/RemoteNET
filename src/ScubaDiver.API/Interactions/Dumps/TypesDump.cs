using System.Collections.Generic;
using ScubaDiver.API.Interactions;

namespace ScubaDiver.API.Interactions.Dumps
{
    public class TypesDump
    {
        public class AssemblyLoadError
        {
            public string Assembly { get; set; }
            public DiverError Error { get; set; }
        }
        public class TypeIdentifiers
        {
            public const uint XorMask = 0xaabbccdd;

            public string Assembly { get; set; }
            public string FullTypeName { get; set; }
            public ulong? XoredMethodTable { get; set; }
            public TypeIdentifiers(string assembly, string fullTypeName, ulong? xoredMethodTable = null)
            {
                this.Assembly = assembly;
                this.FullTypeName = fullTypeName;
                this.XoredMethodTable = xoredMethodTable;
            }
        }
        public List<TypeIdentifiers> Types { get; set; }
        public List<AssemblyLoadError> LoadErrors { get; set; }
    }
}