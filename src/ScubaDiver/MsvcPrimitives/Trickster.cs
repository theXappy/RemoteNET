using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Linq;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.System.Threading;
using Windows.Win32.System.Memory;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Runtime.InteropServices;
using System.Text;
using System.Security.Cryptography;
using ScubaDiver.Rtti;
using ScubaDiver;
using System.Drawing;
// ReSharper disable UnusedMember.Global
// ReSharper disable InconsistentNaming
// ReSharper disable IdentifierTypo

namespace ScubaDiver.Rtti;

public class TricksterException : Exception { }

public record struct FunctionInfo(string mangledName, nuint address);


public abstract class TypeInfo
{
    public static TypeInfo Dummy = new SecondClassTypeInfo("DummyModule", "DummyNamespace", "DummyType");

    public string ModuleName { get; }
    public string Namespace { get; }
    public string Name { get; }
    public string NamespaceAndName => string.IsNullOrWhiteSpace(Namespace) ? $"{Name}" : $"{Namespace}::{Name}";
    public string FullTypeName => $"{ModuleName}!{NamespaceAndName}";

    protected TypeInfo(string moduleName, string @namespace, string name)
    {
        ModuleName = moduleName;
        if (!string.IsNullOrWhiteSpace(@namespace))
            Namespace = @namespace;
        Name = name;
    }
}

/// <summary>
/// Information about a "First-Class Type" - Types which have a full RTTI entry and, most importantly, a vftable.
/// </summary>
public class FirstClassTypeInfo : TypeInfo
{
    public const nuint XorMask = 0xaabbccdd; // Keeping it to 32 bits so it works in both x32 and x64
    public nuint XoredVftableAddress { get; }
    public nuint VftableAddress => XoredVftableAddress ^ XorMask; // A function-based property so the address isn't needlessly kept in memory.
    public nuint Offset { get; }
    public List<nuint> XoredSecondaryVftableAddresses { get; }
    public IEnumerable<nuint> SecondaryVftableAddresses => XoredSecondaryVftableAddresses.Select(x => x ^ XorMask);

    public FirstClassTypeInfo(string moduleName, string @namespace, string name, nuint VftableAddress, nuint Offset) : base(moduleName, @namespace, name)
    {
        XoredVftableAddress = VftableAddress ^ XorMask;
        this.Offset = Offset;
        XoredSecondaryVftableAddresses = new List<nuint>();
    }

    public void AddSecondaryVftable(nuint vftableAddress)
    {
        XoredSecondaryVftableAddresses.Add(vftableAddress ^ XorMask);
    }

    public bool CompareXoredMethodTable(nuint xoredValue, nuint xoredMask)
    {
        return XoredVftableAddress == xoredValue &&
               XorMask == xoredMask;
    }

    public override string ToString()
    {
        return $"{Name} (First Class Type) ({Offset:X16})";
    }
}

/// <summary>
/// Information about a "Second-Class Type" - Types which don't have a full RTTI entry and, most importantly, a vftable.
/// The existence of these types is inferred from their exported functions.
/// If none of the type's methods are exported, we might not know such a type even exists.
/// </summary>
public class SecondClassTypeInfo : TypeInfo
{
    public SecondClassTypeInfo(string moduleName, string @namespace, string name) : base(moduleName, @namespace, name)
    {
    }

    public override string ToString()
    {
        return $"{FullTypeName} (Second Class Type)";
    }
}

public unsafe struct MemoryRegionInfo
{
    public void* BaseAddress;
    public nuint Size;
    public MemoryRegionInfo(void* baseAddress, nuint size) { BaseAddress = baseAddress; Size = size; }
}

public unsafe struct MemoryRegion
{
    public void* Pointer { get; set; }
    public void* BaseAddress { get; set; }
    public nuint Size { get; set; }
    public MemoryRegion(void* pointer, void* baseAddress, nuint size) { Pointer = pointer; BaseAddress = baseAddress; Size = size; }
}

public struct ModuleInfo
{
    public string Name;
    public nuint BaseAddress;
    public nuint Size;
    public ModuleInfo(string name, nuint baseAddress, nuint size)
    {
        Name = name;
        BaseAddress = baseAddress;
        Size = size;
    }

    public override string ToString() => $"{Name} 0x({BaseAddress:x8}, {Size} bytes)";
}

public unsafe class Trickster : IDisposable
{
    public HANDLE _processHandle;

    private ModuleInfo _mainModule;

    private bool _is32Bit;

    public Dictionary<ModuleInfo, List<TypeInfo>> ScannedTypes;
    public Dictionary<ModuleInfo, ModuleOperatorFunctions> OperatorNewFuncs;
    public MemoryRegion[] Regions;

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

    private (bool typeInfoSeen, List<TypeInfo>) ScanTypesCore(ModuleInfo module, List<ModuleSegment> segments, nuint segmentBaseAddress, nuint segmentSize)
    {
        List<TypeInfo> list = new();

        // Whether the "type_info" type was seen.
        // This is a sanity check for RTTI existence. If "type_info"
        // is missing all our "findings" are actually false positives
        bool typeInfoSeen = false;

        using (RttiScanner processMemory = new(_processHandle, module.BaseAddress, module.Size, segments))
        {
            nuint inc = (nuint)(_is32Bit ? 4 : 8);
            Func<ulong, List<ModuleSegment>, string> getClassName = _is32Bit ? processMemory.GetClassName32 : processMemory.GetClassName64;
            for (nuint offset = inc; offset < segmentSize; offset += inc)
            {
                nuint possibleVftableAddress = segmentBaseAddress + offset;
                if (getClassName(possibleVftableAddress, segments) is string fullClassName)
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
            }
        }

        return (typeInfoSeen, list);
    }

    private Dictionary<ModuleInfo, List<TypeInfo>> ScanTypesCore()
    {
        Dictionary<ModuleInfo, List<TypeInfo>> res = new Dictionary<ModuleInfo, List<TypeInfo>>();
        if (ScannedTypes != null)
            res = ScannedTypes;

        Dictionary<ModuleInfo, List<ModuleSegment>> dataSegments = GetAllModulesSegments(res.Keys.ToList());
        foreach (KeyValuePair<ModuleInfo, List<ModuleSegment>> kvp in dataSegments)
        {
            ModuleInfo module = kvp.Key;
            List<ModuleSegment> segments = kvp.Value;

            bool typeInfoSeenInModule = false;
            List<TypeInfo> allModuleTypes = new();
            foreach (ModuleSegment segment in segments)
            {
                try
                {
                    (bool typeInfoSeenInSeg, List<TypeInfo> types) = ScanTypesCore(module, segments, (nuint)segment.BaseAddress, (nuint)segment.Size);

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
                    Console.WriteLine($"[Error] Couldn't scan for RTTI info in {module.Name}, EX: " + ex.GetType().Name);
                }
            }

            if (typeInfoSeenInModule && allModuleTypes.Any())
            {
                if (res.ContainsKey(module))
                {
                    Logger.Debug("[ScanTypesCore] WTF module alraedy exists in final dictionary... ???");
                }
                res[module] = allModuleTypes;
            }
            else
            {
                // No types in this module. Might just be non-MSVC one so we add a dummy
                res[module] = new List<TypeInfo>();
            }
        }

        return res;

        Dictionary<ModuleInfo, List<ModuleSegment>> GetAllModulesSegments(IReadOnlyList<ModuleInfo> skip)
        {
            Dictionary<ModuleInfo, List<ModuleSegment>> dataSegments = new();
            foreach (ModuleInfo modInfo in UnmanagedModules)
            {
                if (skip.Contains(modInfo))
                {
                    Logger.Debug($"Skipping {modInfo.Name} :)");
                    continue;
                }

                List<ModuleSegment> sections = ProcessModuleExtensions.ListSections(modInfo);
                foreach (ModuleSegment moduleSegment in sections)
                {
                    var name = moduleSegment.Name.ToUpperInvariant();
                    // It's probably only ever in ".rdata" but I'm a coward
                    if (name.Contains("DATA") || name.Contains("RTTI"))
                    {
                        if (!dataSegments.ContainsKey(modInfo))
                            dataSegments.Add(modInfo, new List<ModuleSegment>());

                        dataSegments[modInfo].Add(moduleSegment);
                    }
                }
            }

            return dataSegments;
        }
    }


    private MemoryRegionInfo[] ScanRegionInfoCore()
    {
        ulong stop = _is32Bit ? uint.MaxValue : 0x7ffffffffffffffful;
        nuint size = (nuint)sizeof(MEMORY_BASIC_INFORMATION);

        List<MemoryRegionInfo> list = new();

        MEMORY_BASIC_INFORMATION mbi;
        nuint address = 0;


        while (address < stop && PInvoke.VirtualQueryEx(_processHandle, (void*)address, &mbi, size) > 0 && address + mbi.RegionSize > address)
        {
            if (mbi.State == VIRTUAL_ALLOCATION_TYPE.MEM_COMMIT &&
                !mbi.Protect.HasFlag(PAGE_PROTECTION_FLAGS.PAGE_NOACCESS) &&
                !mbi.Protect.HasFlag(PAGE_PROTECTION_FLAGS.PAGE_GUARD) &&
                !mbi.Protect.HasFlag(PAGE_PROTECTION_FLAGS.PAGE_NOCACHE))
                list.Add(new MemoryRegionInfo(mbi.BaseAddress, mbi.RegionSize));
            address += mbi.RegionSize;
        }

        return list.ToArray();
    }

    private MemoryRegion[] ReadRegionsCore(MemoryRegionInfo[] infoArray)
    {
        MemoryRegion[] regionArray = new MemoryRegion[infoArray.Length];
        for (int i = 0; i < regionArray.Length; i++)
        {
            void* baseAddress = infoArray[i].BaseAddress;
            nuint size = infoArray[i].Size;
            void* pointer = NativeMemory.AllocZeroed(size, 1);
            PInvoke.ReadProcessMemory(_processHandle, baseAddress, pointer, size);
            regionArray[i] = new(pointer, baseAddress, size);
        }
        return regionArray;
    }

    public void ReadRegions()
    {
        if (Regions is not null)
        {
            FreeRegionsCore(Regions);
        }

        MemoryRegionInfo[] scannedRegions = ScanRegionInfoCore();
        Regions = ReadRegionsCore(scannedRegions);
    }

    public IDictionary<ulong, IReadOnlyCollection<ulong>> ScanRegions(IEnumerable<nuint> xoredVftables, nuint xorMask = 0x00000000)
    {
        Logger.Debug("[ScanRegions] Fetching Regions for ScanRegionsCore2");
        var regions = Regions;
        Logger.Debug("[ScanRegions] Fetching Regions for ScanRegionsCore2 -- DONE");
        var res = ScanRegionsCore2(Regions, xoredVftables, xorMask);
        Logger.Debug("[ScanRegions] Returned from ScanRegionsCore2");
        return res;
    }

    private void FreeRegionsCore(MemoryRegion[] regionArray)
    {
        // Print call stack to find who called us
        StackTrace stackTrace = new(true);
        Logger.Debug($"[FreeRegionsCore] Called");

        for (int i = 0; i < regionArray.Length; i++)
        {
            CryptographicOperations.ZeroMemory(new Span<byte>(regionArray[i].Pointer, (int)regionArray[i].Size));
            IntPtr intPtr = new(regionArray[i].Pointer);
            //Logger.Debug($"[FreeRegionsCore] Freeing 0x{((ulong)intPtr):X16}");
            NativeMemory.Free(regionArray[i].Pointer);
        }
        Logger.Debug("[FreeRegionsCore] Done freeing regions");
    }
    private IDictionary<ulong, IReadOnlyCollection<ulong>> ScanRegionsCore2(MemoryRegion[] regionArray, IEnumerable<nuint> xoredVftables, nuint xorMask)
    {
        ConcurrentDictionary<ulong, ConcurrentBag<ulong>> results = new();

        foreach (nuint xoredVFtable in xoredVftables)
        {
            results[(ulong)xoredVFtable] = new ConcurrentBag<ulong>();
        }

        Parallel.For(0, regionArray.Length, i =>
        {
            MemoryRegion region = regionArray[i];
            byte* start = (byte*)region.Pointer;
            byte* end = start + region.Size;
            if (_is32Bit)
            {
                for (byte* a = start; a < end; a += 4)
                {
                    ulong suspect = *(uint*)a;
                    if (!results.TryGetValue(suspect ^ xorMask, out var bag))
                        continue;
                    ulong result = (ulong)region.BaseAddress + (ulong)(a - start);
                    bag.Add(result);
                }
            }
            else
            {
                for (byte* a = start; a < end; a += 8)
                {
                    ulong suspect = *(ulong*)a;
                    if (!results.TryGetValue(suspect ^ xorMask, out var bag))
                        continue;
                    ulong result = (ulong)region.BaseAddress + (ulong)(a - start);
                    bag.Add(result);
                }
            }
        });

        Dictionary<ulong, IReadOnlyCollection<ulong>> results2 =
            results.ToDictionary(
                kvp => kvp.Key,
                kvp => (IReadOnlyCollection<ulong>)kvp.Value);
        return results2;
    }

    public class ModuleOperatorFunctions
    {
        public List<nuint> OperatorNewFuncs { get; } = new();
        public List<nuint> OperatorDeleteFuncs { get; } = new();
    }

    private Dictionary<ModuleInfo, ModuleOperatorFunctions> ScanOperatorNewFuncsCore()
    {
        // Start with any existing findings
        OperatorNewFuncs ??= new Dictionary<ModuleInfo, ModuleOperatorFunctions>();
        Dictionary<ModuleInfo, ModuleOperatorFunctions> res = new(OperatorNewFuncs);

        Dictionary<ModuleInfo, List<ModuleSegment>> dataSegments = new();
        foreach (ModuleInfo modInfo in UnmanagedModules)
        {
            if (res.ContainsKey(modInfo))
            {
                // Already processed this module. Modules don't "get" more new functions over time...
                continue;
            }

            List<ModuleSegment> sections;
            try
            {
                sections = ProcessModuleExtensions.ListSections(modInfo);
            }
            catch (AccessViolationException)
            {
                // Probably an unloaded dll
                Logger.Debug($"[Warning] Couldn't list sections of {modInfo.Name} at 0x{modInfo.BaseAddress:x16}");
                continue;
            }

            foreach (ModuleSegment moduleSegment in sections)
            {
                if (!moduleSegment.Name.ToUpperInvariant().Contains(".TEXT"))
                    continue;

                if (!dataSegments.ContainsKey(modInfo))
                    dataSegments.Add(modInfo, new List<ModuleSegment>());

                dataSegments[modInfo].Add(moduleSegment);
            }
        }

        foreach (var kvp in dataSegments)
        {
            ModuleInfo module = kvp.Key;
            var segments = kvp.Value;

            if (!res.ContainsKey(module))
                res[module] = new ModuleOperatorFunctions();

            foreach (ModuleSegment segment in segments)
            {
                try
                {
                    InnerCheckFunc(module.Name, module.BaseAddress, module.Size,
                        segments, (nuint)segment.BaseAddress, (nuint)segment.Size,
                        out List<nuint> newFuncs,
                        out List<nuint> deleteFuncs
                        );
                    res[module].OperatorNewFuncs.AddRange(newFuncs);
                    res[module].OperatorDeleteFuncs.AddRange(deleteFuncs);
                }
                catch (Exception ex)
                {
                    if (ex is not ApplicationException)
                        Console.WriteLine($"[Error] Couldn't scan for 'operator new' in {module.Name}, EX: " + ex.GetType().Name);
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
            List<ModuleSegment> segments, nuint segmentBaseAddress, nuint segmentBSize,
            out List<nuint> operatorNewFuncs,
            out List<nuint> operatorDeleteFuncs)
    {
        operatorNewFuncs = new();
        operatorDeleteFuncs = new();

        byte[] tempData = new byte[opNewEncodedFuncEpilouge_x64_rel.Length];

        using (RttiScanner processMemory = new(_processHandle, moduleBaseAddress, moduleSize, segments))
        {
            nuint inc = (nuint)IntPtr.Size;
            for (nuint offset = inc; offset < segmentBSize; offset += inc)
            {
                nuint address = segmentBaseAddress + offset;
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


    public void Dispose()
    {
        if (Regions is not null)
        {
            FreeRegionsCore(Regions);
        }
    }
}


public class ModuleSegment
{
    public string Name { get; private set; }
    public ulong BaseAddress { get; private set; }
    public ulong Size { get; private set; }

    public ModuleSegment(string name, ulong baseAddress, ulong size)
    {
        Name = name;
        BaseAddress = baseAddress;
        Size = size;
    }

    public override string ToString()
    {
        return string.Format("{0,-8}: 0x{1:X8} - 0x{2:X8} (0x{3:X8} bytes)",
            Name,
            BaseAddress,
            BaseAddress + Size,
            Size);
    }
}


static class ProcessModuleExtensions
{
    // Import the necessary Windows API functions
    [DllImport("kernel32.dll")]
    public static extern uint GetModuleFileName(IntPtr hModule, [Out] char[] lpFileName, int nSize);

    // Function to check if an IntPtr is valid
    public static bool IsIntPtrValid(IntPtr intPtr)
    {
        char[] buffer = new char[256]; // Adjust the buffer size as needed
        uint result = GetModuleFileName(intPtr, buffer, buffer.Length);

        // If GetModuleFileName returns 0, it means the IntPtr is invalid
        if (result == 0)
        {
            int error = Marshal.GetLastWin32Error();
            if (error == 126 /* ERROR_MOD_NOT_FOUND */ || error == 0)
            {
                return false;
            }
            else
            {
                Logger.Debug($"<GetLastWin32Error == {error}>");
            }
        }

        return true;
    }

    public static List<ModuleSegment> ListSections(this ModuleInfo module)
    {
        // Get a pointer to the base address of the module
        IntPtr moduleHandle = new IntPtr((long)module.BaseAddress);

        if (!IsIntPtrValid(moduleHandle))
        {
            Logger.Debug($"[ListSections] == WARNING == Module unloaded! Name: {module.Name} Address: {moduleHandle}");
            return new List<ModuleSegment>();
        }

        // Read the DOS header from the module
        IMAGE_DOS_HEADER dosHeader = Marshal.PtrToStructure<IMAGE_DOS_HEADER>(moduleHandle);

        // Read the PE header from the module
        IntPtr peHeader = new IntPtr(moduleHandle.ToInt64() + dosHeader.e_lfanew);
        IMAGE_NT_HEADERS ntHeaders = Marshal.PtrToStructure<IMAGE_NT_HEADERS>(peHeader);

        // Get a pointer to the section headers in the module
        IntPtr sectionHeaders = new IntPtr(peHeader.ToInt64() + Marshal.SizeOf(typeof(IMAGE_NT_HEADERS))) + 16;

        // Print the details of each section
        List<ModuleSegment> output = new List<ModuleSegment>();
        for (int i = 0; i < ntHeaders.FileHeader.NumberOfSections; i++)
        {
            // Read the section header from the module
            IntPtr sectionHeader = new IntPtr(sectionHeaders.ToInt64() + i * Marshal.SizeOf(typeof(IMAGE_SECTION_HEADER)));
            IMAGE_SECTION_HEADER section = Marshal.PtrToStructure<IMAGE_SECTION_HEADER>(sectionHeader);

            // Print the section details

            // To nearest page boundary
            uint alignment = (uint)ntHeaders.OptionalHeader.SectionAlignment;
            uint roundedVirtualSize = (section.VirtualSize + alignment - 1) & ~(alignment - 1);
            output.Add(
                new ModuleSegment(Encoding.ASCII.GetString(section.Name).TrimEnd('\0'),
                    (ulong)module.BaseAddress + section.VirtualAddress,
                    roundedVirtualSize));
        }

        return output;
    }
}

#pragma warning disable CS0649 // Field is never assigned to, and will always have its default value 0
[StructLayout(LayoutKind.Sequential)]
struct IMAGE_DOS_HEADER
{
    public short e_magic;
    public short e_cblp;
    public short e_cp;
    public short e_crlc;
    public short e_cparhdr;
    public short e_minalloc;
    public short e_maxalloc;
    public short e_ss;
    public short e_sp;
    public short e_csum;
    public short e_ip;
    public short e_cs;
    public short e_lfarlc;
    public short e_ovno;
    [MarshalAs(UnmanagedType.ByValArray, SizeConst = 4)]
    public short[] e_res1;
    public short e_oemid;
    public short e_oeminfo;
    [MarshalAs(UnmanagedType.ByValArray, SizeConst = 10)]
    public short[] e_res2;
    public int e_lfanew;
}

[StructLayout(LayoutKind.Sequential)]
struct IMAGE_NT_HEADERS
{
    public int Signature;
    public IMAGE_FILE_HEADER FileHeader;
    public IMAGE_OPTIONAL_HEADER32 OptionalHeader;
}

[StructLayout(LayoutKind.Sequential)]
struct IMAGE_FILE_HEADER
{
    public short Machine;
    public short NumberOfSections;
    public int TimeDateStamp;
    public int PointerToSymbolTable;
    public int NumberOfSymbols;
    public short SizeOfOptionalHeader;
    public short Characteristics;
}
struct IMAGE_OPTIONAL_HEADER32
{
    public short Magic;
    public byte MajorLinkerVersion;
    public byte MinorLinkerVersion;
    public int SizeOfCode;
    public int SizeOfInitializedData;
    public int SizeOfUninitializedData;
    public int AddressOfEntryPoint;
    public int BaseOfCode;
    public int BaseOfData;
    public int ImageBase;
    public int SectionAlignment;
    public int FileAlignment;
    public short MajorOperatingSystemVersion;
    public short MinorOperatingSystemVersion;
    public short MajorImageVersion;
    public short MinorImageVersion;
    public short MajorSubsystemVersion;
    public short MinorSubsystemVersion;
    public int Win32VersionValue;
    public int SizeOfImage;
    public int SizeOfHeaders;
    public int CheckSum;
    public short Subsystem;
    public short DllCharacteristics;
    public int SizeOfStackReserve;
    public int SizeOfStackCommit;
    public int SizeOfHeapReserve;
    public int SizeOfHeapCommit;
    public int LoaderFlags;
    public int NumberOfRvaAndSizes;
    [MarshalAs(UnmanagedType.ByValArray, SizeConst = 16)]
    public IMAGE_DATA_DIRECTORY[] DataDirectory;
}
struct IMAGE_DATA_DIRECTORY
{
    public uint VirtualAddress;
    public uint Size;
}
struct IMAGE_SECTION_HEADER
{
    [MarshalAs(UnmanagedType.ByValArray, SizeConst = 8)]
    public byte[] Name;
    public uint VirtualSize;
    public uint VirtualAddress;
    public uint SizeOfRawData;
    public uint PointerToRawData;
    public uint PointerToRelocations;
    public uint PointerToLineNumbers;
    public ushort NumberOfRelocations;
    public ushort NumberOfLineNumbers;
    public uint Characteristics;
}
#pragma warning restore CS0649 // Field 'IMAGE_DATA_DIRECTORY.Size' is never assigned to, and will always have its default value 0