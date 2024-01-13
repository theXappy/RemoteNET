using System.Collections.Generic;
using System.Linq;
using NtApiDotNet.Win32;

namespace ScubaDiver;

public class ExportsMaster : IReadOnlyExportsMaster
{
    private Dictionary<string, List<DllExport>> _exportsCache = new();
    private IReadOnlyList<DllExport> GetExportsInner(string moduleName)
    {
        if (!_exportsCache.ContainsKey(moduleName))
        {
            var lib = SafeLoadLibraryHandle.GetModuleHandle(moduleName);
            _exportsCache[moduleName] = lib.Exports.ToList();
        }
        return _exportsCache[moduleName];
    }


    private Dictionary<Rtti.ModuleInfo, List<UndecoratedSymbol>> _undecExportsCache = new();
    public IReadOnlyList<UndecoratedSymbol> GetExports(Rtti.ModuleInfo modInfo)
    {
        if (!_undecExportsCache.ContainsKey(modInfo))
        {
            IReadOnlyList<DllExport> exports = GetExportsInner(modInfo.Name);
            IEnumerable<UndecoratedSymbol> undecExports = exports
                .Select(exp => exp.TryUndecorate(modInfo, out var undecExp) ? undecExp : null)
                .Where(exp => exp != null);
            _undecExportsCache[modInfo] = undecExports.ToList();
        }
        return _undecExportsCache[modInfo];
    }

    /// <summary>
    /// Get a specific type from a specific module.
    /// </summary>
    public IEnumerable<UndecoratedSymbol> GetExportedTypeMembers(Rtti.ModuleInfo module, string typeFullName)
    {
        string membersPrefix = $"{typeFullName}::";
        return GetExports(module).Where(sym => sym.UndecoratedFullName.StartsWith(membersPrefix));
    }
    public IEnumerable<UndecoratedFunction> GetExportedTypeFunctions(Rtti.ModuleInfo module, string typeFullName)
    {
        return GetExportedTypeMembers(module, typeFullName).OfType<UndecoratedFunction>();
    }

}