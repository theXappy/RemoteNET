using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using Windows.Win32.Foundation;
using ScubaDiver.Rtti;
using ScubaDiver.API.Utils;
using TypeInfo = ScubaDiver.Rtti.TypeInfo;
using NtApiDotNet.Win32;
using System.Threading;
using System.Threading.Tasks;

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

        _refreshTask = new Task(() =>
        {
            try
            {
                RefreshMonitorTask();
            }
            catch (Exception ex)
            {
                Logger.Debug($"[TricksterWrapper][!!!] Exception in RefreshMonitorTask: {ex}");
                Logger.Debug($"[TricksterWrapper][!!!] Exception in RefreshMonitorTask: {ex}");
                Logger.Debug($"[TricksterWrapper][!!!] Exception in RefreshMonitorTask: {ex}");
                Logger.Debug($"[TricksterWrapper][!!!] Exception in RefreshMonitorTask: {ex}");
            }
        });
    }

    public void Refresh()
    {
        lock (_tricksterLock)
        {
            Logger.Debug($"[{DateTime.Now}][MsvcDiver][Trickster0] Refreshing runtime!");
            Stopwatch sw = Stopwatch.StartNew();
            Dictionary<RichModuleInfo, ModuleOperatorFunctions> operatorNewFuncs = null;
            if (_trickster != null)
            {
                operatorNewFuncs = _trickster.OperatorNewFuncs;
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
            _trickster.ScanOperatorNewFuncs();
            secondary.Stop();
            Logger.Debug($"[{DateTime.Now}][MsvcDiver][Trickster] DONE ScanOperatorNewFuncs Elapsed: {secondary.ElapsedMilliseconds} ms");

            sw.Stop();
            Logger.Debug($"[{DateTime.Now}][MsvcDiver][Trickster] DONE refreshing runtime. Num Modules: {_trickster.ScannedTypes.Count}.");
            Logger.Debug($"[{DateTime.Now}][MsvcDiver][Trickster] DONE refreshing runtime. *** Total Elapsed: {sw.ElapsedMilliseconds} ms");

            // Update "identifiers"
            Process p = Process.GetCurrentProcess();
            _lastRefreshModulesCount = p.Modules.Count;
            _lastRefreshModules = p.Modules.Cast<ProcessModule>()
                .Select(pModule => pModule.ModuleName)
                .ToHashSet();
        }
    }

    public bool TryGetOperatorNew(RichModuleInfo richModule, out List<nuint> operatorNewAddresses)
    {
        lock (_tricksterLock)
        {
            if (_trickster.OperatorNewFuncs.TryGetValue(richModule, out var moduleFuncs))
            {
                operatorNewAddresses = moduleFuncs.OperatorNewFuncs;
                return true;
            }
            operatorNewAddresses = null;
            return false;
        }
    }

    public Dictionary<RichModuleInfo, List<TypeInfo>> GetDecoratedTypes()
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
            Dictionary<RichModuleInfo, List<TypeInfo>> modulesAndTypes = GetDecoratedTypes();
            if (!modulesAndTypes.Any(m => moduleNameFilter(m.Key.ModuleInfo.Name)))
            {
                // No modules pass the filter... Try again after a refresh.
                Refresh();
                modulesAndTypes = GetDecoratedTypes();
            }

            List<UndecoratedModule> output = new();
            foreach (KeyValuePair<RichModuleInfo, List<TypeInfo>> kvp in modulesAndTypes)
            {
                // First check if module passes the filter
                RichModuleInfo module = kvp.Key;
                ModuleInfo moduleInfo = module.ModuleInfo;
                if (!moduleNameFilter(moduleInfo.Name))
                    continue;

                // Check in the cache if we already processed this module
                if (!_undecModeulesCache.TryGetValue(moduleInfo.Name, out UndecoratedModule undecModule))
                {
                    // Unprocessed module. Processing now...
                    // Generate the undecorated module and save in cache
                    undecModule = GenerateUndecoratedModule(module, kvp.Value);
                    _undecModeulesCache[module.ModuleInfo.Name] = undecModule;
                }

                // store this module for the output
                output.Add(undecModule);
            }

            return output;
        }
    }

    private UndecoratedModule GenerateUndecoratedModule(RichModuleInfo richModule, List<TypeInfo> firstClassTypes)
    {
        lock (_tricksterLock)
        {
            UndecoratedModule undecModule = new UndecoratedModule(richModule.ModuleInfo.Name, richModule);

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
            HashSet<UndecoratedSymbol> allUndecoratedExports = ExportsMaster.GetUndecoratedExports(undecModule.ModuleInfo).ToHashSet();
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
                TypeInfo ti = new SecondClassTypeInfo(undecModule.Name, namespaceName, typeName);
                // Store aside as a member of this type
                allClassTypes.Add(ti.FullTypeName, ti);
                allClassTypesNames.Add(nameAndNamespace);
            }

            // Let's find "static" exported classes
            HashSet<string> staticClassNames = new();
            foreach (UndecoratedSymbol undecSymbol in allUndecoratedExports)
            {
                if (undecSymbol is not UndecoratedFunction ctor)
                    continue;

                string undecExportName = ctor.UndecoratedFullName;
                IsCtorName(undecExportName, out int lastDoubleColonIndex);
                if (lastDoubleColonIndex == -1)
                {
                    // No C++ name format: namespace::classname
                    continue;
                }

                string nameAndNamespace = undecExportName[..lastDoubleColonIndex];
                if (allClassTypesNames.Contains(nameAndNamespace))
                {
                    // This export belond to a previously found first-class/second-class type (last loop)
                    continue;
                }

                if (staticClassNames.Contains(nameAndNamespace))
                {
                    // Already found this static class (this loop)
                    continue;
                }

                // Split fullTypeName to namespace & type name
                int lastIndexOfColonColon = nameAndNamespace.LastIndexOf("::");
                // take into consideration that "::" might no be present at all, and the namespace is empty
                string namespaceName = lastIndexOfColonColon == -1 ? "" : nameAndNamespace.Substring(0, lastIndexOfColonColon);
                string typeName = lastIndexOfColonColon == -1 ? nameAndNamespace : nameAndNamespace.Substring(lastIndexOfColonColon + 2);

                // NEW 2nd-class type. Adding a new match!
                TypeInfo ti = new SecondClassTypeInfo(undecModule.Name, namespaceName, typeName);
                // Store aside as a member of this type
                allClassTypes.Add(ti.FullTypeName, ti);
                staticClassNames.Add(nameAndNamespace);
            }


            // Now iterate all class Types & search any exports that match their names
            foreach (TypeInfo typeInfo in allClassTypes.Values)
            {
                // $#@!: Is this a hack?
                undecModule.GetOrAddType(typeInfo);

                // Find all exported members of the type
                IEnumerable<UndecoratedSymbol> methods = ExportsMaster.GetExportedTypeMembers(undecModule.ModuleInfo, typeInfo.NamespaceAndName);
                foreach (UndecoratedSymbol symbol in methods)
                {
                    if (symbol is not UndecoratedExportedFunc undecFunc) // TODO: Fields
                        continue;

                    // Store aside as a member of this type
                    undecModule.AddTypeFunction(typeInfo, undecFunc);

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

                undecModule.AddUndecoratedTypelessFunction(undecFunc);
            }

            // Which means C-style, non-class-associated funcs/variables.
            List<DllExport> leftoverFunc = ExportsMaster.GetLeftoverExports(undecModule.ModuleInfo).ToList();
            foreach (DllExport export in leftoverFunc)
            {
                undecModule.AddRegularTypelessFunction(export);
            }


            // 'operator new' are most likely not exported. We need the trickster to tell us where they are.
            if (TryGetOperatorNew(richModule, out List<nuint> operatorNewAddresses))
            {
                foreach (nuint operatorNewAddr in operatorNewAddresses)
                {
                    UndecoratedFunction undecFunction =
                        new UndecoratedInternalFunction(
                            undecModule.ModuleInfo,
                            undecoratedName: "operator new",
                            undecoratedFullName: "operator new",
                            decoratedName: "operator new",
                            operatorNewAddr, 
                            1,
                            "void*");
                    // TODO: Add this is a "regular" typeless func
                    undecModule.AddUndecoratedTypelessFunction(undecFunction);
                }
            }

            return undecModule;
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
    private object _refreshRequiredLock = new object();
    private bool _refreshRequired = false;
    private Task _refreshTask = null;

    public void RefreshMonitorTask()
    {
        while(true)
        {
            // STILL need an old refresh, no reason to re-check
            if (_refreshRequired == true)
                continue;
            lock (_refreshRequiredLock)
            {
                if (_refreshRequired == true)
                    continue;
                _refreshRequired = InternalCheck();
            }

            Thread.Sleep(1000);
        }

        bool InternalCheck()
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

                return false;
            }
        }
    }

    public bool RefreshRequired()
    {
        lock (_refreshRequiredLock)
        {
            if (_refreshRequired)
            {
                _refreshRequired = false;
                _refreshTask.Start();
                return true;
            }
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
}