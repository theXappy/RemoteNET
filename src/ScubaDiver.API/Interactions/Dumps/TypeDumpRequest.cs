namespace ScubaDiver.API.Interactions.Dumps
{
    /// <summary>
    /// Either use Assembly+TypeFullName -or- MethodTableAddress
    /// </summary>
    public class TypeDumpRequest
    {
        public string Assembly { get; set; }
        public string TypeFullName { get; set; }
        public ulong XoredMethodTableAddress { get; set; }
    }
}