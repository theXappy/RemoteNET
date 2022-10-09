using System.Collections.Generic;

namespace ScubaDiver.API.Interactions.Dumps
{
    public enum ObjectType
    {
        Unknown,
        Primitive,
        NonPrimitive,
        Array
    }

    public class ObjectDump
    {
        public ObjectType ObjectType { get; set; }
        public ObjectType SubObjectsType { get; set; }

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
        /// <summary>
        /// Number of elemnets in the array. This field is only meaningful if ObjectType is "Array"
        /// </summary>
        public int SubObjectsCount { get; set; }
        public List<MemberDump> Fields { get; set; }
        public List<MemberDump> Properties { get; set; }
        public int HashCode { get; set; }

        public ObjectDump()
        {

        }
    }
}