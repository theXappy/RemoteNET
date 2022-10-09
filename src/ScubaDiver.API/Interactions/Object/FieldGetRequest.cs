namespace ScubaDiver.API.Interactions.Object
{
    public class FieldGetRequest
    {
        public ulong ObjAddress { get; set; }
        public string TypeFullName { get; set; }
        public string FieldName { get; set; }
    }
}