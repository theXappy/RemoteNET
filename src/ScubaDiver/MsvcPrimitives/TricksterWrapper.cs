using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using Windows.Win32.Foundation;
using ScubaDiver.Rtti;
using ScubaDiver.API.Utils;
using TypeInfo = ScubaDiver.Rtti.TypeInfo;
using NtApiDotNet.Win32;

namespace ScubaDiver;

public class TricksterWrapper
{
    private Trickster _trickster;
    private object _tricksterLock;
    private Dictionary<string, UndecoratedModule> _undecModeulesCache = new Dictionary<string, UndecoratedModule>();
    public ExportsMaster ExportsMaster { get; set; }

    public TricksterWrapper()
    {
        _trickster = null;
        _tricksterLock = new object();
        ExportsMaster = new ExportsMaster();
    }

    public void Refresh()
    {
        lock (_tricksterLock)
        {
            Logger.Debug($"[{DateTime.Now}][MsvcDiver][Trickster0] Refreshing runtime!");
            Stopwatch sw = Stopwatch.StartNew();
            Dictionary<ModuleInfo, Trickster.ModuleOperatorFunctions> operatorNewFuncs = null;
            if (_trickster != null)
            {
                operatorNewFuncs = _trickster.OperatorNewFuncs;
                _trickster.Dispose();
                _trickster = null;
            }

            _trickster = new Trickster(Process.GetCurrentProcess());
            _trickster.OperatorNewFuncs = operatorNewFuncs;

            // Logger.Debug($"[{DateTime.Now}][MsvcDiver][Trickster0] Scanning types...");
            Stopwatch secondary = Stopwatch.StartNew();
            _trickster.ScanTypes();
            secondary.Stop();
            Logger.Debug($"[{DateTime.Now}][MsvcDiver][Trickster] DONE ScanTypes Elapsed: {secondary.ElapsedMilliseconds} ms");

            secondary.Restart();
            _trickster.ReadRegions();
            secondary.Stop();
            Logger.Debug($"[{DateTime.Now}][MsvcDiver][Trickster] DONE ReadRegions Elapsed: {secondary.ElapsedMilliseconds} ms");

            secondary.Restart();
            _trickster.ScanOperatorNewFuncs();
            secondary.Stop();
            Logger.Debug($"[{DateTime.Now}][MsvcDiver][Trickster] DONE ScanOperatorNewFuncs Elapsed: {secondary.ElapsedMilliseconds} ms");

            sw.Stop();
            Logger.Debug($"[{DateTime.Now}][MsvcDiver][Trickster] DONE refreshing runtime. Num Modules: {_trickster.ScannedTypes.Count}. Total Elapsed: {sw.ElapsedMilliseconds} ms");

            // Update "identifiers"
            Process p = Process.GetCurrentProcess();
            _lastRefreshModulesCount = p.Modules.Count;
            _lastRefreshModules = p.Modules.Cast<ProcessModule>()
                .Select(pModule => pModule.ModuleName)
                .ToHashSet();
        }
    }

    public bool TryGetOperatorNew(ModuleInfo moduleInfo, out List<nuint> operatorNewAddresses)
    {
        lock (_tricksterLock)
        {
            if (_trickster.OperatorNewFuncs.TryGetValue(moduleInfo, out var moduleFuncs))
            {
                operatorNewAddresses = moduleFuncs.OperatorNewFuncs;
                return true;
            }
            operatorNewAddresses = null;
            return false;
        }
    }

    public Dictionary<ModuleInfo, List<TypeInfo>> GetDecoratedTypes()
    {
        lock (_tricksterLock)
        {
            if (_trickster == null)
                Refresh();

            return _trickster.ScannedTypes;
        }
    }

    public List<ModuleInfo> GetModules()
    {
        lock (_tricksterLock)
        {
            return _trickster.UnmanagedModules;
        }
    }

    public List<ModuleInfo> GetModules(Predicate<string> filter)
    {
        lock (_tricksterLock)
        {
            return _trickster.UnmanagedModules.Where(a => filter(a.Name)).ToList();
        }
    }

    public List<ModuleInfo> GetModules(string name) => GetModules(s => s == name);


    /// <summary>
    /// Returns ALL modules as UndecoratedModule
    /// </summary>
    public List<UndecoratedModule> GetUndecoratedModules() => GetUndecoratedModules(_ => true);

    /// <summary>
    /// Return some modules according to a given filter
    /// </summary>
    public List<UndecoratedModule> GetUndecoratedModules(Predicate<string> moduleNameFilter)
    {
        lock (_tricksterLock)
        {
            Dictionary<ModuleInfo, List<TypeInfo>> modulesAndTypes = GetDecoratedTypes();
            if (!modulesAndTypes.Any(m => moduleNameFilter(m.Key.Name)))
            {
                // No modules pass the filter... Try again after a refresh.
                Refresh();
                modulesAndTypes = GetDecoratedTypes();
            }

            List<UndecoratedModule> output = new();
            foreach (KeyValuePair<ModuleInfo, List<TypeInfo>> kvp in modulesAndTypes)
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
    }

    private UndecoratedModule GenerateUndecoratedModule(ModuleInfo moduleInfo, List<TypeInfo> firstClassTypes)
    {
        lock (_tricksterLock)
        {
            UndecoratedModule module = new UndecoratedModule(moduleInfo.Name, moduleInfo);

            Dictionary<string, TypeInfo> allClassTypes = new();
            foreach (TypeInfo curr in firstClassTypes)
            {
                FirstClassTypeInfo currFirstClassType = curr as FirstClassTypeInfo;

                if (allClassTypes.TryGetValue(curr.FullTypeName, out TypeInfo collectedClassType))
                {
                    (collectedClassType as FirstClassTypeInfo).AddSecondaryVftable(currFirstClassType.VftableAddress);
                }
                else
                {
                    allClassTypes[curr.FullTypeName] = new FirstClassTypeInfo(
                        currFirstClassType.ModuleName,
                        currFirstClassType.Namespace,
                        currFirstClassType.Name,
                        currFirstClassType.VftableAddress,
                        currFirstClassType.Offset);
                }
            }
            HashSet<string> allClassTypesNames = allClassTypes.Select(x => x.Value.NamespaceAndName).ToHashSet();

            // Collect 2nd-class types & removing ALL ctors from the exports list (for 1st or 2nd class types).

            // First, going over exports and looking for constructors.
            // Getting all UNEDCORATED exports, type funcs and typeless
            HashSet<UndecoratedSymbol> allUndecoratedExports = ExportsMaster.GetUndecoratedExports(moduleInfo).ToHashSet();
            foreach (UndecoratedSymbol undecSymbol in allUndecoratedExports)
            {
                if (undecSymbol is not UndecoratedFunction ctor)
                    continue;

                string undecExportName = ctor.UndecoratedFullName;
                if (!IsCtorName(undecExportName, out int lastDoubleColonIndex))
                    continue;

                string nameAndNamespace = undecExportName[..lastDoubleColonIndex];
                if (allClassTypesNames.Contains(nameAndNamespace))
                {
                    // This is a previously found first-class/second-class type.
                    continue;
                }

                // Split fullTypeName to namespace & type name
                int lastIndexOfColonColon = nameAndNamespace.LastIndexOf("::");
                // take into consideration that "::" might no be present at all, and the namespace is empty
                string namespaceName = lastIndexOfColonColon == -1 ? "" : nameAndNamespace.Substring(0, lastIndexOfColonColon);
                string typeName = lastIndexOfColonColon == -1 ? nameAndNamespace : nameAndNamespace.Substring(lastIndexOfColonColon + 2);

                // NEW 2nd-class type. Adding a new match!
                TypeInfo ti = new SecondClassTypeInfo(module.Name, namespaceName, typeName);
                // Store aside as a member of this type
                allClassTypes.Add(ti.FullTypeName, ti);
                allClassTypesNames.Add(nameAndNamespace);
            }


            // Now iterate all class Types & search any exports that match their names
            foreach (TypeInfo typeInfo in allClassTypes.Values)
            {
                // $#@!: Is this a hack?
                module.GetOrAddType(typeInfo);

                // Find all exported members of the type
                IEnumerable<UndecoratedSymbol> methods = ExportsMaster.GetExportedTypeMembers(moduleInfo, typeInfo.NamespaceAndName);
                foreach (UndecoratedSymbol symbol in methods)
                {
                    if (symbol is not UndecoratedExportedFunc undecFunc) // TODO: Fields
                        continue;

                    // Store aside as a member of this type
                    module.AddTypeFunction(typeInfo, undecFunc);

                    // Removing type func from allExports
                    allUndecoratedExports.Remove(undecFunc);
                }
            }

            // This list should now hold only typeless symbols.
            // Which means C++-style, non-class-associated funcs/variables.
            foreach (UndecoratedSymbol export in allUndecoratedExports)
            {
                if (export is not UndecoratedFunction undecFunc)
                {
                    //// Logger.Debug("Typeless-export which isn't a function is discarded. Undecorated name: " + export.UndecoratedFullName);
                    continue;
                }

                module.AddUndecoratedTypelessFunction(undecFunc);
            }

            // Which means C-style, non-class-associated funcs/variables.
            List<DllExport> leftoverFunc = ExportsMaster.GetLeftoverExports(moduleInfo).ToList();
            foreach (DllExport export in leftoverFunc)
            {
                module.AddRegularTypelessFunction(export);
            }


            // 'operator new' are most likely not exported. We need the trickster to tell us where they are.
            if (TryGetOperatorNew(moduleInfo, out List<nuint> operatorNewAddresses))
            {
                foreach (nuint operatorNewAddr in operatorNewAddresses)
                {
                    UndecoratedFunction undecFunction =
                        new UndecoratedInternalFunction(
                            undecoratedName: "operator new",
                            undecoratedFullName: "operator new",
                            decoratedName: "operator new",
                            operatorNewAddr, 1,
                            moduleInfo);
                    // TODO: Add this is a "regular" typeless func
                    module.AddUndecoratedTypelessFunction(undecFunction);
                }
            }

            return module;
        }


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

    private int _lastRefreshModulesCount = 0;
    private HashSet<string> _lastRefreshModules = new HashSet<string>();
    public bool RefreshRequired()
    {
        lock (_tricksterLock)
        {
            if (_trickster == null || !_trickster.ScannedTypes.Any())
            {
                Logger.Debug("[TricksterWrapper.RefreshRequired] Refresh is required because Trickster is null or no types were scanned.");
                return true;
            }

            Process p = Process.GetCurrentProcess();
            if (p.Modules.Count != _lastRefreshModulesCount)
            {
                Logger.Debug("[TricksterWrapper.RefreshRequired] Refresh is required because module count changed.");
                return true;
            }
            var currModules = p.Modules.Cast<ProcessModule>()
                .Select(pModule => pModule.ModuleName)
                .ToHashSet();
            if (!currModules.SetEquals(_lastRefreshModules))
            {
                Logger.Debug("[TricksterWrapper.RefreshRequired] Refresh is required because module names changed.");
                return true;
            }

            Logger.Debug("[TricksterWrapper.RefreshRequired] Refresh is NOT required.");
            return false;
        }
    }

    public HANDLE GetProcessHandle()
    {
        lock (_tricksterLock)
        {
            return _trickster._processHandle;
        }
    }

    public Dictionary<FirstClassTypeInfo, IReadOnlyCollection<ulong>> Scan(IEnumerable<FirstClassTypeInfo> allClassesToScanFor)
    {
        lock (_tricksterLock)
        {
            return TricksterScanHelper.Scan(_trickster, allClassesToScanFor);
        }
    }
}