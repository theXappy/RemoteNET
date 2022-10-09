namespace ScubaDiver.API.Interactions.Object
{
    public class IndexedItemAccessRequest
    {
        public ulong CollectionAddress { get; set; }
        public bool PinRequest { get; set; }
        public ObjectOrRemoteAddress Index { get; set; }
    }

}