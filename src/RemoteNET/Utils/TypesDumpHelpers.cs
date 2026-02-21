using System.Collections.Generic;
using System.Linq;
using ScubaDiver.API.Interactions.Dumps;

namespace RemoteNET.Utils
{
    public static class TypesDumpHelpers
    {
        public static List<CandidateType> QueryTypes(RemoteApp app, string typeFullNameFilter, out List<TypesDump.AssemblyLoadError> loadErrors)
        {
            if (app is ManagedRemoteApp managedApp)
            {
                TypesDump dump = managedApp.Communicator.DumpTypes(typeFullNameFilter, out loadErrors);
                List<TypesDump.TypeIdentifiers> typeIdentifiers = dump?.Types ?? new List<TypesDump.TypeIdentifiers>();
                return ToCandidateTypes(typeIdentifiers);
            }

            loadErrors = new List<TypesDump.AssemblyLoadError>();
            return app.QueryTypes(typeFullNameFilter).ToList();
        }

        public static ulong? UnmaskMethodTable(ulong? xoredMethodTable)
        {
            if (!xoredMethodTable.HasValue)
                return null;
            return xoredMethodTable ^ TypesDump.TypeIdentifiers.XorMask;
        }

        public static List<CandidateType> ToCandidateTypes(IEnumerable<TypesDump.TypeIdentifiers> typeIdentifiers)
        {
            List<CandidateType> results = new();
            foreach (TypesDump.TypeIdentifiers type in typeIdentifiers)
            {
                ulong? methodTable = UnmaskMethodTable(type.XoredMethodTable);
                results.Add(new CandidateType(RuntimeType.Managed, type.FullTypeName, type.Assembly, methodTable));
            }
            return results;
        }
    }
}
