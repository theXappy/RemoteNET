using System.Collections.Generic;

namespace ScubaDiver
{
    public class ObjectDump
    {
        public ulong Address { get; set; }
        public string Type { get; set; }
        public string PrimitiveValue { get; set; }
        public List<MemberDump> Fields { get; set; }
        public List<MemberDump> Properties { get; set; }
    }
}