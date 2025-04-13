using NtApiDotNet.Win32;
using System.Collections.Generic;

namespace ScubaDiver;

internal interface IReadOnlyExportsMaster
{
    public void ProcessExports(Rtti.ModuleInfo modInfo);

    // Everything
    public IReadOnlyList<DllExport> GetExports(Rtti.ModuleInfo modInfo);

    // Split into Undecorated and "other"
    public ICollection<UndecoratedSymbol> GetUndecoratedExports(Rtti.ModuleInfo modInfo);
    public IEnumerable<DllExport> GetLeftoverExports(Rtti.ModuleInfo modInfo);

    // By specific type
    public IEnumerable<UndecoratedSymbol> GetExportedTypeMembers(Rtti.ModuleInfo module, string typeFullName);
    public IEnumerable<UndecoratedFunction> GetExportedTypeFunctions(Rtti.ModuleInfo module, string typeFullName);
}