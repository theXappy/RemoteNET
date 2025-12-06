using System;
using System.Diagnostics;
using System.Linq;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.System.Threading;
using System.Collections.Generic;

// ReSharper disable UnusedMember.Global
// ReSharper disable InconsistentNaming
// ReSharper disable IdentifierTypo

namespace ScubaDiver.Rtti;

public unsafe class Trickster
{
    public HANDLE _processHandle;

    private ModuleInfo _mainModule;

    private bool _is32Bit;

    public Dictionary<RichModuleInfo, List<TypeInfo>> ScannedTypes;
    public Dictionary<RichModuleInfo, ModuleOperatorFunctions> OperatorNewFuncs;

    public List<ModuleInfo> UnmanagedModules;


    public Trickster(Process process)
    {
        _processHandle = PInvoke.OpenProcess(PROCESS_ACCESS_RIGHTS.PROCESS_ALL_ACCESS, true, (uint)process.Id);
        if (_processHandle.IsNull) throw new TricksterException();

        var mainModuleName = process.MainModule.ModuleName;
        var mainModuleBaseAddress = (nuint)process.MainModule.BaseAddress.ToPointer();
        var mainModuleSize = (nuint)process.MainModule.ModuleMemorySize;
        _mainModule = new ModuleInfo(mainModuleName, mainModuleBaseAddress, mainModuleSize);

        var modules = process.Modules;
        var managedAssemblies = AppDomain.CurrentDomain.GetAssemblies();
        var ManagedAssembliesLocations = managedAssemblies
            .Where(assm => !assm.IsDynamic)
            .Select(assm => assm.Location).ToList();

        UnmanagedModules = modules.Cast<ProcessModule>()
            .Where(pModule => !ManagedAssembliesLocations.Contains(pModule.FileName))
            .Select(pModule => new ModuleInfo(pModule.ModuleName, (nuint)(nint)pModule.BaseAddress, (nuint)(nint)pModule.ModuleMemorySize))
            .ToList();

        BOOL is32Bit;
        PInvoke.IsWow64Process(_processHandle, &is32Bit);
        _is32Bit = is32Bit;
    }

    private (bool typeInfoSeen, List<TypeInfo>) ScanTypesCore(RichModuleInfo richModule, ModuleSection currSection)
    {
        nuint sectionBaseAddress = (nuint)currSection.BaseAddress;
        nuint sectionSize = (nuint)currSection.Size;
        List<TypeInfo> list = new();

        // Whether the "type_info" type was seen.
        // This is a sanity check for RTTI existence. If "type_info"
        // is missing all our "findings" are actually false positives
        bool typeInfoSeen = false;

        ModuleInfo module = richModule.ModuleInfo;
        IReadOnlyList<ModuleSection> sections = richModule.Sections;

        // Used to FORCE the change of the vftable var value in the loop
        nuint dummySum = 0;
        using (RttiScanner processMemory = new(_processHandle, module.BaseAddress, module.Size, sections))
        {
            nuint inc = (nuint)(_is32Bit ? 4 : 8);
            Func<ulong, IReadOnlyList<ModuleSection>, string> getClassName = _is32Bit ? processMemory.GetClassName32 : processMemory.GetClassName64;
            for (nuint offset = inc; offset < sectionSize; offset += inc)
            {
                nuint possibleVftableAddress = sectionBaseAddress + offset;
                if (getClassName(possibleVftableAddress, sections) is string fullClassName)
                {
                    // Avoiding names with the "BEL" control ASCII char specifically.
                    // The heuristic search in this method finds a lot of garbage, but this one is particularly 
                    // annoying because trying to print any type's "name" containing
                    // "BEL" to the console will trigger a *ding* sound.
                    if (fullClassName.Contains('\a'))
                        continue;

                    if (fullClassName == "type_info")
                        typeInfoSeen = true;

                    // split fullClassName into namespace and class name
                    int lastIndexOfColonColon = fullClassName.LastIndexOf("::");
                    // take into consideration that "::" might no be present at all, and the namespace is empty
                    string namespaceName = lastIndexOfColonColon == -1 ? "" : fullClassName.Substring(0, lastIndexOfColonColon);
                    string typeName = lastIndexOfColonColon == -1 ? fullClassName : fullClassName.Substring(lastIndexOfColonColon + 2);

                    list.Add(new FirstClassTypeInfo(module.Name, namespaceName, typeName, possibleVftableAddress, offset));
                }

                // Destroy false positives by moving to the next possible vftable address
                possibleVftableAddress ^= 0xa5a5a5a5;
                dummySum += possibleVftableAddress; // So the compiler doesn't optimize the above line out
            }
        }

        // Use the dummySum to avoid compiler optimizations
        dummySum.ToString();

        return (typeInfoSeen, list);
    }

    private Dictionary<RichModuleInfo, List<TypeInfo>> ScanTypesCore()
    {
        Dictionary<RichModuleInfo, List<TypeInfo>> res = new Dictionary<RichModuleInfo, List<TypeInfo>>();
        IReadOnlyList<RichModuleInfo> skip = ScannedTypes?.Keys.ToList() ?? new List<RichModuleInfo>();

        List<RichModuleInfo> dataSections = GetRichModules(skip);
        foreach (RichModuleInfo richModule in dataSections)
        {
            Func<ModuleSection, bool> filter = (s) => s.Name.ToUpper().Contains("DATA") ||
                                                      s.Name.ToUpper().Contains("RTTI");
            IReadOnlyList<ModuleSection> sections = richModule.GetSections(filter).ToList();

            bool typeInfoSeenInModule = false;
            List<TypeInfo> allModuleTypes = new();
            foreach (ModuleSection section in sections)
            {
                try
                {
                    (bool typeInfoSeenInSeg, List<TypeInfo> types) = ScanTypesCore(richModule, section);

                    typeInfoSeenInModule = typeInfoSeenInModule || typeInfoSeenInSeg;

                    if (typeInfoSeenInModule)
                    {
                        if (allModuleTypes.Count == 0)
                        {
                            // optimized(?) - swap lists instead of copy
                            allModuleTypes = types;
                        }
                        else
                        {
                            allModuleTypes.AddRange(types);
                        }
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[Error] Couldn't scan for RTTI info in {richModule.ModuleInfo.Name}, EX: " + ex.GetType().Name);
                }
            }

            if (typeInfoSeenInModule && allModuleTypes.Any())
            {
                if (res.ContainsKey(richModule))
                {
                    Logger.Debug("[ScanTypesCore] WTF module alraedy exists in final dictionary... ???");
                }
                res[richModule] = allModuleTypes;
            }
            else
            {
                // No types in this module. Might just be non-MSVC one so we add a dummy
                res[richModule] = new List<TypeInfo>();
            }
        }

        return res;
    }
    private List<RichModuleInfo> GetRichModules(IReadOnlyList<RichModuleInfo> richModulesToSkip)
    {
        HashSet<ModuleInfo> modulesToSkip = richModulesToSkip?.Select(rmi => rmi.ModuleInfo).ToHashSet() ?? [];

        List<RichModuleInfo> richModules = new List<RichModuleInfo>();
        foreach (ModuleInfo modInfo in UnmanagedModules)
        {
            if (modulesToSkip.Contains(modInfo))
            {
                continue;
            }

            IReadOnlyList<ModuleSection> sections = ProcessModuleExtensions.ListSections(modInfo);
            richModules.Add(new RichModuleInfo(modInfo, sections.ToList()));
        }
        return richModules;
    }

    private Dictionary<RichModuleInfo, ModuleOperatorFunctions> ScanOperatorNewFuncsCore()
    {
        // Start with any existing findings
        OperatorNewFuncs ??= new Dictionary<RichModuleInfo, ModuleOperatorFunctions>();

        List<RichModuleInfo> richModules = GetRichModules(OperatorNewFuncs.Keys.ToList());

        Func<ModuleSection, bool> textFilter = (s) => s.Name.ToUpper() == ".TEXT";
        Dictionary<RichModuleInfo, List<ModuleSection>> dataSections =
            richModules.ToDictionary(
                    keySelector: module => module,
                    elementSelector: module => module.GetSections(".TEXT").ToList());

        Dictionary<RichModuleInfo, ModuleOperatorFunctions> res = new(OperatorNewFuncs);
        foreach (var kvp in dataSections)
        {
            RichModuleInfo module = kvp.Key;
            ModuleInfo moduleInfo = module.ModuleInfo;
            List<ModuleSection> sections = kvp.Value;

            if (!res.ContainsKey(module))
                res[module] = new ModuleOperatorFunctions();

            foreach (ModuleSection section in sections)
            {
                try
                {
                    InnerCheckFunc(moduleInfo.Name, moduleInfo.BaseAddress, moduleInfo.Size,
                        sections, (nuint)section.BaseAddress, (nuint)section.Size,
                        out List<nuint> newFuncs,
                        out List<nuint> deleteFuncs
                        );
                    res[module].OperatorNewFuncs.AddRange(newFuncs);
                    res[module].OperatorDeleteFuncs.AddRange(deleteFuncs);
                }
                catch (Exception ex)
                {
                    if (ex is not ApplicationException)
                        Console.WriteLine($"[Error] Couldn't scan for 'operator new' in {moduleInfo.Name}, EX: " + ex.GetType().Name);
                }
            }
        }
        return res;
    }


    public static readonly byte[] opNewEncodedFuncEpilouge_x64_rel = new byte[]
    {
            0x40, 0x53, 0x48, 0x83,
            0xEC, 0x20, 0x48, 0x8B,
            0xD9, 0xEB, 0x0F
    };
    public const ulong _first8bytes = 0x8B4820EC83485340;

    void InnerCheckFunc(string moduleName, nuint moduleBaseAddress, nuint moduleSize,
            List<ModuleSection> sections, nuint sectionBaseAddress, nuint sectionSize,
            out List<nuint> operatorNewFuncs,
            out List<nuint> operatorDeleteFuncs)
    {
        operatorNewFuncs = new();
        operatorDeleteFuncs = new();

        byte[] tempData = new byte[opNewEncodedFuncEpilouge_x64_rel.Length];

        using (RttiScanner processMemory = new(_processHandle, moduleBaseAddress, moduleSize, sections))
        {
            nuint inc = (nuint)IntPtr.Size;
            for (nuint offset = inc; offset < sectionSize; offset += inc)
            {
                nuint address = sectionBaseAddress + offset;
                if (!processMemory.TryRead<byte>(address, tempData))
                    continue;
                if (tempData.AsSpan().SequenceEqual(opNewEncodedFuncEpilouge_x64_rel.AsSpan()))
                {
                    operatorNewFuncs.Add(address);
                }
            }
        }
    }


    public void ScanTypes()
    {
        ScannedTypes = ScanTypesCore();
    }

    public void ScanOperatorNewFuncs()
    {
        OperatorNewFuncs = ScanOperatorNewFuncsCore();
    }
}
