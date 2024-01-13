using System.Collections.Generic;

namespace ScubaDiver;

internal interface IReadOnlyExportsMaster
{
    public IReadOnlyList<UndecoratedSymbol> GetExports(Rtti.ModuleInfo modInfo);
    public IEnumerable<UndecoratedSymbol> GetExportedTypeMembers(Rtti.ModuleInfo module, string typeFullName);
    public IEnumerable<UndecoratedFunction> GetExportedTypeFunctions(Rtti.ModuleInfo module, string typeFullName);
}