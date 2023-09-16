using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using Windows.Win32.Foundation;
using NtApiDotNet.Win32;
using ScubaDiver.Rtti;

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

    public Dictionary<ModuleInfo, TypeInfo[]> GetDecoratedModules()
    {
        return _trickster.ScannedTypes;
    }
    public TypeInfo[] GetAllTypes()
    {
        return GetDecoratedModules().SelectMany(x => x.Value).ToArray();
    }

    public List<ModuleInfo> GetModules() => _trickster.ModulesParsed;
    public List<ModuleInfo> GetModules(Predicate<string> filter) => _trickster.ModulesParsed.Where(a => filter(a.Name)).ToList();
    public List<ModuleInfo> GetModules(string name) => GetModules(s => s == name);

    private Dictionary<string, UndecoratedModule> _undecModeulesCache = new Dictionary<string, UndecoratedModule>();
    public List<UndecoratedModule> GetUndecoratedModules(Predicate<string> filter) => GetUndecoratedModules().Where(a => filter(a.Name)).ToList();
    public List<UndecoratedModule> GetUndecoratedModules()
    {
        UndecoratedModule GenerateUndecoratedModule(ModuleInfo moduleInfo, TypeInfo[] types)
        {
            // Getting all exports, type funcs and typeless
            List<UndecoratedSymbol> allExports = _exports.GetUndecoratedExports(moduleInfo).ToList();

            UndecoratedModule module = new UndecoratedModule(moduleInfo.Name, moduleInfo);

            // Now iterate all first-class Types
            foreach (TypeInfo typeInfo in types)
            {
                // Find all exported members of the first-class type
                IEnumerable<UndecoratedSymbol> methods = _exports.GetExportedTypeMembers(moduleInfo, typeInfo.Name);
                foreach (UndecoratedSymbol symbol in methods)
                {
                    if (symbol is UndecoratedExportedFunc undecFunc)
                    {
                        // Store aside as a member of this type
                        module.AddTypeFunction(typeInfo, undecFunc);

                        // Removing type func from allExports
                        allExports.Remove(undecFunc);
                    }
                }
            }

            // This list should now hold only typeless symbols. Which means C-style, non-class-associated funcs/variables or
            // second-class types' members.
            foreach (var export in allExports)
            {
                if (export is not UndecoratedFunction undecFunc)
                {
                    Logger.Debug("Typless-export which isn't a function is discarded. Undecorated name: " + export.UndecoratedFullName);
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
        }

        Refresh();
        Dictionary<ModuleInfo, TypeInfo[]> modulesAndTypes = GetDecoratedModules();

        List<UndecoratedModule> output = new();
        foreach (KeyValuePair<ModuleInfo, TypeInfo[]> kvp in modulesAndTypes)
        {
            var module = kvp.Key;
            if (!_undecModeulesCache.TryGetValue(module.Name, out UndecoratedModule undecModule))
            {
                // Generate the undecorated module and save in cache
                undecModule = GenerateUndecoratedModule(module, kvp.Value);
                _undecModeulesCache[module.Name] = undecModule;
            }
            // store this module for the output
            output.Add(undecModule);
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