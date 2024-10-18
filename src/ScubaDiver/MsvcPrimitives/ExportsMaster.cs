using System.Collections.Generic;
using System.Linq;
using NtApiDotNet.Win32;
using ScubaDiver.Demangle.Demangle.Core.Hll.Pascal;

namespace ScubaDiver;

public class ExportsMaster : IReadOnlyExportsMaster
{
    private Dictionary<string, List<DllExport>> _exportsCache = new();

    private Dictionary<Rtti.ModuleInfo, List<UndecoratedSymbol>> _undecExportsCache = new();
    private Dictionary<Rtti.ModuleInfo, List<DllExport>> _leftoverExportsCache = new();

    public IReadOnlyList<DllExport> GetExports(Rtti.ModuleInfo modInfo)
        => GetExports(modInfo.Name);
    public IReadOnlyList<DllExport> GetExports(string moduleName)
    {
        if (!_exportsCache.ContainsKey(moduleName))
        {
            var lib = SafeLoadLibraryHandle.GetModuleHandle(moduleName);
            _exportsCache[moduleName] = lib.Exports.ToList();
        }
        return _exportsCache[moduleName];
    }

    public IEnumerable<UndecoratedSymbol> GetUndecoratedExports(Rtti.ModuleInfo modInfo)
    {
        ProcessExports(modInfo);
        return _undecExportsCache[modInfo];
    }

    public IEnumerable<DllExport> GetLeftoverExports(Rtti.ModuleInfo modInfo)
    {
        ProcessExports(modInfo);
        return _leftoverExportsCache[modInfo];
    }
    

    public void ProcessExports(Rtti.ModuleInfo modInfo)
    {
        if (_undecExportsCache.ContainsKey(modInfo) &&
            _leftoverExportsCache.ContainsKey(modInfo))
        {
            // Already processed
            return;
        }

        IReadOnlyList<DllExport> exports = GetExports(modInfo);
        List<UndecoratedSymbol> undecoratedExports = new List<UndecoratedSymbol>();
        List<DllExport> leftoverExports = new List<DllExport>();
        foreach (DllExport export in exports)
        {
            // C++ mangled names will be successfully undecorated.
            // The rest are "C-Style", class-less, exports.
            if (export.TryUndecorate(modInfo, out var undecExp))
                undecoratedExports.Add(undecExp);
            else
                leftoverExports.Add(export);
        }
        _undecExportsCache[modInfo] = undecoratedExports.ToList();
        _leftoverExportsCache[modInfo] = leftoverExports.ToList();
    }

    /// <summary>
    /// Get a specific type from a specific module.
    /// </summary>
    public IEnumerable<UndecoratedSymbol> GetExportedTypeMembers(Rtti.ModuleInfo module, string typeFullName)
    {
        string membersPrefix = $"{typeFullName}::";
        ProcessExports(module);
        return _undecExportsCache[module].Where(sym => sym.UndecoratedFullName.StartsWith(membersPrefix));
    }
    public IEnumerable<UndecoratedFunction> GetExportedTypeFunctions(Rtti.ModuleInfo module, string typeFullName)
    {
        return GetExportedTypeMembers(module, typeFullName).OfType<UndecoratedFunction>();
    }

}