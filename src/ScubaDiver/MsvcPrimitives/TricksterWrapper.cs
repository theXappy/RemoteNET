using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using Windows.Win32.Foundation;
using NtApiDotNet.Win32;
using ScubaDiver.Rtti;
using ScubaDiver.API.Utils;
using System.Reflection;
using TypeInfo = ScubaDiver.Rtti.TypeInfo;

namespace ScubaDiver;

public class TricksterWrapper
{
    private Trickster _trickster;
    private ExportsMaster _exports;

    public TricksterWrapper(ExportsMaster exports)
    {
        _trickster = null;
        _exports = exports;
    }

    public void Refresh()
    {
        Logger.Debug($"[{DateTime.Now}][MsvcDiver][Trickster0] Refreshing runtime!");
        if (_trickster != null)
        {
            _trickster.Dispose();
            _trickster = null;
        }
        _trickster = new Trickster(Process.GetCurrentProcess());
        Logger.Debug($"[{DateTime.Now}][MsvcDiver][Trickster0] Scanning types...");
        _trickster.ScanTypes();
        Logger.Debug($"[{DateTime.Now}][MsvcDiver][Trickster0] Done.");
        Logger.Debug($"[{DateTime.Now}][MsvcDiver][Trickster0] Reading Regions...");
        _trickster.ReadRegions();
        Logger.Debug($"[{DateTime.Now}][MsvcDiver][Trickster] Done.");
        Logger.Debug($"[{DateTime.Now}][MsvcDiver][Trickster0] Scan 'new' operators...");
        _trickster.ScanOperatorNewFuncs();
        Logger.Debug($"[{DateTime.Now}][MsvcDiver][Trickster] Done.");
        Logger.Debug($"[{DateTime.Now}][MsvcDiver][Trickster] DONE refreshing runtime. Num Modules: {_trickster.ScannedTypes.Count}");
    }

    public bool TryGetOperatorNew(ModuleInfo moduleInfo, out nuint[] operatorNewAddresses)
    {
        return _trickster.OperatorNewFuncs.TryGetValue(moduleInfo, out operatorNewAddresses);
    }

    public Dictionary<ModuleInfo, TypeInfo[]> GetDecoratedTypes()
    {
        return _trickster.ScannedTypes;
    }

    public List<ModuleInfo> GetModules() => _trickster.ModulesParsed;
    public List<ModuleInfo> GetModules(Predicate<string> filter) => _trickster.ModulesParsed.Where(a => filter(a.Name)).ToList();
    public List<ModuleInfo> GetModules(string name) => GetModules(s => s == name);

    private Dictionary<string, UndecoratedModule> _undecModeulesCache = new Dictionary<string, UndecoratedModule>();
    public List<UndecoratedModule> GetUndecoratedModules() => GetUndecoratedModules(_ => true);
    public List<UndecoratedModule> GetUndecoratedModules(Predicate<string> moduleNameFilter)
    {
        Refresh();
        Dictionary<ModuleInfo, TypeInfo[]> modulesAndTypes = GetDecoratedTypes();

        List<UndecoratedModule> output = new();
        foreach (KeyValuePair<ModuleInfo, TypeInfo[]> kvp in modulesAndTypes)
        {
            // First check if module passes the filter
            ModuleInfo module = kvp.Key;
            if (!moduleNameFilter(module.Name))
                continue;

            // Check in the cache if we already processed this module
            if (!_undecModeulesCache.TryGetValue(module.Name, out UndecoratedModule undecModule))
            {
                // Unprocessed module. Processing now...
                // Generate the undecorated module and save in cache
                undecModule = GenerateUndecoratedModule(module, kvp.Value);
                _undecModeulesCache[module.Name] = undecModule;
            }

            // store this module for the output
            output.Add(undecModule);
        }

        return output;
    }

    private UndecoratedModule GenerateUndecoratedModule(ModuleInfo moduleInfo, TypeInfo[] firstClassTypes)
    {
        UndecoratedModule module = new UndecoratedModule(moduleInfo.Name, moduleInfo);

        List<TypeInfo> allClassTypes = firstClassTypes.ToList();
        HashSet<string> allClassTypesNames = allClassTypes.Select(x => x.Name).ToHashSet();

        // Collect 2nd-class types & removing ALL ctors from the exports list (for 1st or 2nd class types).
        // First, going over exports and looking for constructors.
        // Getting all exports, type funcs and typeless
        List<UndecoratedSymbol> allExports = _exports.GetExports(moduleInfo).ToList();
        foreach (UndecoratedSymbol undecSymbol in allExports)
        {
            if (undecSymbol is not UndecoratedFunction ctor)
                continue;

            string undecExportName = ctor.UndecoratedFullName;
            if (!IsCtorName(undecExportName, out int lastDoubleColonIndex))
                continue;

            string fullTypeName = undecExportName[..lastDoubleColonIndex];
            if (allClassTypesNames.Contains(fullTypeName))
            {
                // This is a previously found first-class/second-class type.
                continue;
            }

            // NEW 2nd-class type. Adding a new match!
            TypeInfo ti = new SecondClassTypeInfo(module.Name, fullTypeName);
            // Store aside as a member of this type
            allClassTypes.Add(ti);
            allClassTypesNames.Add(ti.Name);
        }


        // Now iterate all class Types & search any exports that match their names
        foreach (TypeInfo typeInfo in allClassTypes)
        {
            // Find all exported members of the type
            IEnumerable<UndecoratedSymbol> methods = _exports.GetExportedTypeMembers(moduleInfo, typeInfo.Name);
            foreach (UndecoratedSymbol symbol in methods)
            {
                if (symbol is not UndecoratedExportedFunc undecFunc) // TODO: Fields
                    continue;

                // Store aside as a member of this type
                module.AddTypeFunction(typeInfo, undecFunc);

                // Removing type func from allExports
                allExports.Remove(undecFunc);
            }
        }

        // This list should now hold only typeless symbols.
        // Which means C-style, non-class-associated funcs/variables.
        foreach (UndecoratedSymbol export in allExports)
        {
            if (export is not UndecoratedFunction undecFunc)
            {
                Logger.Debug("Typeless-export which isn't a function is discarded. Undecorated name: " + export.UndecoratedFullName);
                continue;
            }

            module.AddTypelessFunction(undecFunc);
        }

        // 'operator new' are most likely not exported. We need the trickster to tell us where they are.
        if (TryGetOperatorNew(moduleInfo, out nuint[] operatorNewAddresses))
        {
            foreach (nuint operatorNewAddr in operatorNewAddresses)
            {
                UndecoratedFunction undecFunction =
                    new UndecoratedInternalFunction(
                        undecoratedName: "operator new",
                        undecoratedFullName: "operator new",
                        decoratedName: "operator new",
                        (long)operatorNewAddr, 1,
                        moduleInfo);
                module.AddTypelessFunction(undecFunction);
            }
        }

        return module;


        bool IsCtorName(string fullName, out int lastDoubleColonIndex)
        {
            lastDoubleColonIndex = fullName.LastIndexOf("::");
            if (lastDoubleColonIndex == -1)
                return false;

            string className = fullName.Substring(lastDoubleColonIndex + 2 /* :: */);
            // Check if this class is in a namespace.
            if (fullName.EndsWith($"::{className}::{className}"))
                return true;
            // Edge case: A class outside any namespace.
            if (fullName == $"{className}::{className}")
                return true;
            return false;
        }
    }

    public IReadOnlyDictionary<ModuleInfo, IEnumerable<TypeInfo>> SearchTypes(string rawAssemblyFilter, string rawTypeFilter)
    {
        Predicate<string> assmNameFilter = Filter.CreatePredicate(rawAssemblyFilter);
        Predicate<string> typeNameFilter = Filter.CreatePredicate(rawTypeFilter);
        Func<TypeInfo, bool> typeFilter = ti => typeNameFilter(ti.Name);

        Dictionary<ModuleInfo, IEnumerable<TypeInfo>> output = new();
        foreach (UndecoratedModule module in GetUndecoratedModules(assmNameFilter))
        {
            IEnumerable<TypeInfo> matchingTypes = module.Types.Where(ti => typeFilter(ti));

            if (matchingTypes.Any())
                output[module.ModuleInfo] = matchingTypes;
        }

        return output;

    }

    public bool RefreshRequired()
    {
        return _trickster == null || !_trickster.ScannedTypes.Any();
    }

    public HANDLE GetProcessHandle()
    {
        return _trickster._processHandle;
    }

    public Dictionary<FirstClassTypeInfo, IReadOnlyCollection<ulong>> Scan(IEnumerable<FirstClassTypeInfo> allClassesToScanFor)
    {
        Dictionary<FirstClassTypeInfo, IReadOnlyCollection<ulong>> addresses = TricksterUI.Scan(_trickster, allClassesToScanFor);
        return addresses;
    }
}