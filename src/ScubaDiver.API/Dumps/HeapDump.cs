using System.Collections.Generic;

namespace ScubaDiver.API.Dumps
{
    public class HeapDump
    {
        public class HeapObject
        {
            public ulong Address { get; set; }
            public string Type { get; set; }
        }

        public List<HeapObject> Objects { get; set; }
    }
}