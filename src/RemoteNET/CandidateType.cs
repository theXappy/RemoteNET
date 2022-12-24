namespace RemoteNET
{
    public class CandidateType
    {
        public string TypeFullName { get; set; }
        public string Assembly { get; set; }

        public CandidateType(string typeName, string assembly)
        {
            TypeFullName = typeName;
            Assembly = assembly;
        }
    }
}
