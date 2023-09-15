using System.Collections.Generic;
using System.Linq;
using NtApiDotNet.Win32;

namespace ScubaDiver;

public class ExportsMaster
{
    private Dictionary<string, List<DllExport>> _exportsCache = new();
    public IReadOnlyList<DllExport> GetExports(string moduleName)
    {
        if (!_exportsCache.ContainsKey(moduleName))
        {
            var lib = SafeLoadLibraryHandle.GetModuleHandle(moduleName);
            _exportsCache[moduleName] = lib.Exports.ToList();
        }
        return _exportsCache[moduleName];
    }

    private Dictionary<Rtti.ModuleInfo, List<UndecoratedSymbol>> _undecExportsCache = new Dictionary<Rtti.ModuleInfo, List<UndecoratedSymbol>>();
    private IReadOnlyList<UndecoratedSymbol> GetUndecoratedExports(Rtti.ModuleInfo modInfo)
    {
        if (!_undecExportsCache.ContainsKey(modInfo))
        {
            IReadOnlyList<DllExport> exports = GetExports(modInfo.Name);
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
        IReadOnlyList<UndecoratedSymbol> exports = GetUndecoratedExports(module);
        foreach (UndecoratedSymbol symb in exports)
        {
            if (symb.UndecoratedFullName.StartsWith(membersPrefix))
            {
                yield return symb;
            }
        }
    }
    public IEnumerable<UndecoratedFunction> GetExportedTypeFunctions(Rtti.ModuleInfo module, string typeFullName)
    {
        foreach (UndecoratedSymbol symbol in GetExportedTypeMembers(module, typeFullName))
        {
            if (symbol is UndecoratedFunction undecFunc)
            {
                yield return undecFunc;
            }
        }
    }

}