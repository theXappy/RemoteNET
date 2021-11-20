namespace RemoteNET
{
    /// <summary>
    /// A candidate for a remote object.
    /// Holding this item does not mean having a meaningful hold of the remote object. To gain one use <see cref="RemoteApp"/>
    /// </summary>
    public class CandidateObject
    {
        public ulong Address { get; set; }
        public string TypeFullName { get; set; }
        public int HashCode { get; private set; }

        public CandidateObject(ulong address, string typeFullName, int hashCode)
        {
            Address = address;
            TypeFullName = typeFullName;
            HashCode = hashCode;
        }
    }
}
