using ScubaDiver.Utils;

namespace ScubaDiver
{
    public class ObjectOrRemoteAddress
    {
        /// <summary>
        /// Whether <see cref="RemoteAddress"/> or <see cref="EncodedObject"/> are set.
        /// </summary>
        public bool IsRemoteAddress { get; set; }

        public string Type { get; set; }
        public ulong RemoteAddress { get; set; }
        public string EncodedObject{ get; set; }

        public static ObjectOrRemoteAddress FromObj(object o) => new() {EncodedObject = PrimitivesEncoder.Encode(o), Type = o.GetType().FullName};

        public static ObjectOrRemoteAddress FromToken(ulong addr, string type) =>
            new() {IsRemoteAddress = true, RemoteAddress = addr, Type = type};
    }
}