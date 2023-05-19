using System;
using System.Collections;
using System.Diagnostics;
using System.Linq;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.System.Threading;
using Windows.Win32.System.Memory;
using System.Collections.Generic;
using System.Reflection;
using System.Threading.Tasks;
using System.Threading;
using System.Runtime.InteropServices;
using System.Text;
using ScubaDiver.Utils;

namespace ScubaDiver.Rtti;

public class TricksterException : Exception { }

public record struct TypeInfo(string Name, nuint Address, nuint Offset)
{
    public override string ToString()
    {
        return $"{Name} - {Offset:X}";
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
    public void* Pointer;
    public void* BaseAddress;
    public nuint Size;
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
    private HANDLE _processHandle;

    private ModuleInfo _mainModule;

    private bool _is32Bit;

    public Dictionary<ModuleInfo, TypeInfo[]> ScannedTypes;
    public MemoryRegion[] Regions;
    public List<ModuleInfo> ModulesParsed;

    public Trickster(Process process)
    {
        _processHandle = Kernel32.OpenProcess(PROCESS_ACCESS_RIGHTS.PROCESS_ALL_ACCESS, true, (uint)process.Id);
        if (_processHandle.IsNull) throw new TricksterException();

        var mainModuleName = process.MainModule.ModuleName;
        var mainModuleBaseAddress = (nuint)process.MainModule.BaseAddress.ToPointer();
        var mainModuleSize = (nuint)process.MainModule.ModuleMemorySize;
        _mainModule = new ModuleInfo(mainModuleName, mainModuleBaseAddress, mainModuleSize);

        var modules = process.Modules;
        var managedAssemblies = AppDomain.CurrentDomain.GetAssemblies();
        var managedAssembliesNames = managedAssemblies
            .Where(assm => !assm.IsDynamic)
            .Select(assm => assm.Location).ToList();

        ModulesParsed = modules.Cast<ProcessModule>()
            .Select(pModule => new ModuleInfo(pModule.ModuleName, (nuint)(nint)pModule.BaseAddress, (nuint)(nint)pModule.ModuleMemorySize))
            .ToList();

        BOOL is32Bit;
        Kernel32.IsWow64Process(_processHandle, &is32Bit);
        _is32Bit = is32Bit;
    }

    private TypeInfo[] ScanTypesCore(nuint moduleBaseAddress, nuint moduleSize, nuint segmentBaseAddress, nuint segmentBSize)
    {
        List<TypeInfo> list = new();

        // Whether the "type_info" type was seen.
        // This is a sanity check for RTTI existence. If "type_info"
        // is missing all our "findings" are actually false positives
        bool typeInfoSeen = false;

        using (RttiScanner processMemory = new(_processHandle, moduleBaseAddress, moduleSize))
        {
            nuint inc = (nuint)(_is32Bit ? 4 : 8);
            Func<ulong, string> getClassName = _is32Bit ? processMemory.GetClassName32 : processMemory.GetClassName64;
            for (nuint offset = inc; offset < segmentBSize; offset += inc)
            {
                nuint address = segmentBaseAddress + offset;
                if (getClassName(address) is string className)
                {
                    if (className == "type_info")
                        typeInfoSeen = true;
                    list.Add(new TypeInfo(className, address, offset));
                }
            }
        }

        return typeInfoSeen ? list.ToArray() : Array.Empty<TypeInfo>();
    }

    private Dictionary<ModuleInfo, TypeInfo[]> ScanTypesCore()
    {
        Dictionary<ModuleInfo, TypeInfo[]> res = new Dictionary<ModuleInfo, TypeInfo[]>();

        Dictionary<ModuleInfo, List<ModuleSegment>> dataSegments = new();
        foreach (ModuleInfo modInfo in ModulesParsed.Prepend(_mainModule))
        {
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

        foreach (var kvp in dataSegments)
        {
            var module = kvp.Key;
            var segments = kvp.Value;

            foreach (ModuleSegment segment in segments)
            {
                try
                {
                    var types = ScanTypesCore(module.BaseAddress, module.Size, (nuint)segment.BaseAddress, (nuint)segment.Size);
                    if (types.Length > 0)
                    {
                        if (!res.ContainsKey(module))
                            res[module] = Array.Empty<TypeInfo>();
                        res[module] = res[module].Concat(types).ToArray();
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[Error] Couldn't scan for RTTI info in {module.Name}, EX: " + ex.GetType().Name);
                }
            }
        }

        return res;
    }

    private MemoryRegionInfo[] ScanRegionInfoCore()
    {
        ulong stop = _is32Bit ? uint.MaxValue : 0x7ffffffffffffffful;
        nuint size = (nuint)sizeof(MEMORY_BASIC_INFORMATION);

        List<MemoryRegionInfo> list = new();

        MEMORY_BASIC_INFORMATION mbi;
        nuint address = 0;


        while (address < stop && Kernel32.VirtualQueryEx(_processHandle, (void*)address, &mbi, size) > 0 && address + mbi.RegionSize > address)
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
            void* pointer = NativeMemory.Alloc(size);
            Kernel32.ReadProcessMemory(_processHandle, baseAddress, pointer, size);
            regionArray[i] = new(pointer, baseAddress, size);
        }
        return regionArray;
    }

    private void FreeRegionsCore(MemoryRegion[] regionArray)
    {
        for (int i = 0; i < regionArray.Length; i++)
        {
            NativeMemory.Free(regionArray[i].Pointer);
        }
    }

    private ulong[] ScanRegionsCore(MemoryRegion[] regionArray, ulong value)
    {
        List<ulong> list = new();

        Parallel.For(0, regionArray.Length, i =>
        {
            MemoryRegion region = regionArray[i];
            byte* start = (byte*)region.Pointer;
            byte* end = start + region.Size;
            if (_is32Bit)
            {
                for (byte* a = start; a < end; a += 4)
                    if (*(uint*)a == value)
                        lock (list)
                        {
                            ulong result = (ulong)region.BaseAddress + (ulong)(a - start);
                            list.Add(result);
                        }
            }
            else
            {
                for (byte* a = start; a < end; a += 8)
                    if (*(ulong*)a == value)
                        lock (list)
                        {
                            ulong result = (ulong)region.BaseAddress + (ulong)(a - start);
                            list.Add(result);
                        }
            }
        });

        return list.ToArray();
    }

    public void ScanTypes()
    {
        ScannedTypes = ScanTypesCore();
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

    public ulong[] ScanRegions(ulong value)
    {
        return ScanRegionsCore(Regions, value);
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
        return string.Format("{0,-8}: 0x{1:X8} - 0x{2:X8} ({3} bytes)",
            Name,
            BaseAddress,
            BaseAddress + Size,
            Size);
    }
}


static class ProcessModuleExtensions
{
    public static List<ModuleSegment> ListSections(this ModuleInfo module)
    {
        // Get a pointer to the base address of the module
        IntPtr moduleHandle = new IntPtr((long)module.BaseAddress);

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

            output.Add(
                new ModuleSegment(Encoding.ASCII.GetString(section.Name).TrimEnd('\0'),
                    (ulong)module.BaseAddress + section.VirtualAddress,
                    section.VirtualSize));
        }

        return output;
    }
}

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
    public uint PointerToLinenumbers;
    public ushort NumberOfRelocations;
    public ushort NumberOfLinenumbers;
    public uint Characteristics;
}