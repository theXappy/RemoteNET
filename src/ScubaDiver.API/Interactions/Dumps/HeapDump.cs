using System.Collections.Generic;

namespace ScubaDiver.API.Interactions.Dumps
{
    public class HeapDump
    {
        public class HeapObject
        {
            public ulong Address { get; set; }
            public string Type { get; set; }
            public int HashCode { get; set; }
            public ulong MethodTable { get; set; }
        }

        public List<HeapObject> Objects { get; set; } = new();

    }
}