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
            public ulong XoredMethodTable { get; set; }
            public ulong XorMask { get; set; }
            public ulong MethodTable() => XoredMethodTable ^ XorMask;
        }

        public List<HeapObject> Objects { get; set; } = new();

    }
}