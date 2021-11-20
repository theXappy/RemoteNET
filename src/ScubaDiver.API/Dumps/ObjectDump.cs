using System.Collections.Generic;

namespace ScubaDiver.API.Dumps
{
    public class ObjectDump
    {
        /// <summary>
        /// Address where the item was retrieved from
        /// </summary>
        public ulong RetrivalAddress { get; set; }
        /// <summary>
        /// Address when the item was freezed at when pinning. This address won't change until unpinning.
        /// </summary>
        public ulong PinnedAddress { get; set; }
        public string Type { get; set; }
        public string PrimitiveValue { get; set; }
        public List<MemberDump> Fields { get; set; }
        public List<MemberDump> Properties { get; set; }
        public int HashCode { get; set; }
    }
}