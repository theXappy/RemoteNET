namespace RemoteNET
{
    public class CandidateType
    {
        public string TypeFullName { get; set; }
        public string Assembly { get; set; }
        public ulong MethodTable { get; set; }
        public int Token { get; private set; }


        public CandidateType(string typeName, string assembly, ulong methodTable, int token)
        {
            TypeFullName = typeName;
            Assembly = assembly;
            MethodTable = methodTable;
            Token = token;
        }
    }
}
