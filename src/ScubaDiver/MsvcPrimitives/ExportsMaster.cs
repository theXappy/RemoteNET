using System.Collections.Generic;
using System.Linq;
using NtApiDotNet.Win32;

namespace ScubaDiver;

public class ExportsMaster : IReadOnlyExportsMaster
{
    private Dictionary<string, List<DllExport>> _exportsCache = new();
    private Dictionary<string, List<DllImport>> _importsCache = new();

    private Dictionary<Rtti.ModuleInfo, List<UndecoratedSymbol>> _undecExportsCache = new();
    private Dictionary<Rtti.ModuleInfo, List<DllExport>> _leftoverExportsCache = new();


    public void LoadExportsImports(string moduleName)
    {
        if (!_exportsCache.ContainsKey(moduleName))
        {
            try
            {
                var lib = SafeLoadLibraryHandle.GetModuleHandle(moduleName);
                _exportsCache[moduleName] = lib.Exports.ToList();
                _importsCache[moduleName] = lib.Imports.ToList();

            }
            catch (NtApiDotNet.Win32.SafeWin32Exception ex)
            {
                if (ex.Message == "The specified module could not be found.")
                {
                    // fuck it
                    _exportsCache[moduleName] = new List<DllExport>();
                    _importsCache[moduleName] = new List<DllImport>();
                }
                else
                {
                    throw;
                }
            }
        }
    }
    public IReadOnlyList<DllExport> GetExports(string moduleName)
    {
        LoadExportsImports(moduleName);
        return _exportsCache[moduleName];
    }

    public IReadOnlyList<DllExport> GetExports(Rtti.ModuleInfo modInfo) => GetExports(modInfo.Name);

    public IReadOnlyList<DllImport> GetImports(string moduleName)
    {
        LoadExportsImports(moduleName);
        if (_importsCache.TryGetValue(moduleName, out var list))
        {
            return list;
        }
        return null;
    }

    public IReadOnlyList<DllImport> GetImports(Rtti.ModuleInfo modInfo) => GetImports(modInfo.Name);

    public IReadOnlyList<UndecoratedSymbol> GetUndecoratedExports(Rtti.ModuleInfo modInfo)
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

    public UndecoratedSymbol QueryExportByAddress(nuint address)
    {
        nuint valueAtAddress = 0; // TODO: Read content at <address>, avoiding access violations!!
        nuint xoredValue = (nuint)(valueAtAddress) ^ UndecoratedExportedField.XorMask;
        uint ordinal = 0; // TODO: Read ordinal at <address + ptr_size>, avoiding access violations!!

        foreach (KeyValuePair<Rtti.ModuleInfo, List<UndecoratedSymbol>> kvp in _undecExportsCache)
        {
            Rtti.ModuleInfo module = kvp.Key;
            if (module.BaseAddress > address || address >= module.BaseAddress + module.Size)
                continue;

            foreach (UndecoratedSymbol export in kvp.Value)
            {
                if (export.XoredAddress != xoredValue)
                    continue;
                if (export is not UndecoratedExportedField undecField)
                    continue;
                if (undecField.Export.Ordinal != ordinal)
                    continue;

                return export;
            }
        }

        return null;
    }
}
