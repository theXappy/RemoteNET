namespace ScubaDiver.API.Dumps
{
    public class FieldSetRequest
    {
        public ulong ObjAddress { get; set; }
        public string TypeFullName { get; set; }
        public string FieldName { get; set; }
        public ObjectOrRemoteAddress Value { get; set; }
    }
}