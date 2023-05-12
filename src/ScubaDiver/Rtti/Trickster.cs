using System;
using System.Collections;
using System.Diagnostics;
using System.Linq;

using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.System.Threading;
using Windows.Win32.System.Memory;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Threading;
using System.Runtime.InteropServices;

namespace TheLeftExit.Trickster.Memory
{
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
            ModulesParsed = modules.Cast<ProcessModule>()
                .Select(pModule => new ModuleInfo(pModule.ModuleName, (nuint)(nint)pModule.BaseAddress, (nuint)(nint)pModule.ModuleMemorySize))
                .ToList();

            BOOL is32Bit;
            Kernel32.IsWow64Process(_processHandle, &is32Bit);
            _is32Bit = is32Bit;
        }

        private TypeInfo[] ScanTypesCore(nuint moduleBaseAddress, nuint moduleSize)
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
                for (nuint offset = inc; offset < moduleSize; offset += inc)
                {
                    nuint address = moduleBaseAddress + offset;
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

            TypeInfo[] mainBaseDict = ScanTypesCore(_mainModule.BaseAddress, _mainModule.Size);
            res[_mainModule] = mainBaseDict;


            foreach (ModuleInfo modInfo in ModulesParsed)
            {
                //if (modInfo.Name.Contains("libSpen"))
                {
                    try
                    {
                        res[modInfo] = ScanTypesCore(modInfo.BaseAddress, modInfo.Size);
                    }
                    catch
                    {
                        Debug.WriteLine($"[Error] Couldn't scan for RTTI info in {modInfo.Name}");
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

        private bool isNotPrintable(char c) => c <= ' ' || c >= '~';
        private bool isPrintable(char c) => !isNotPrintable(c);

        public void ScanTypes()
        {
            Dictionary<ModuleInfo, TypeInfo[]> scannedModules = ScanTypesCore();

            //var lolz = scannedModules.ToDictionary(
            //    kvp => kvp.Key,
            //    kvp => kvp.Value.Where(ti => ti.Name.All(isPrintable)).ToArray());

            //var scannedTypes = scannedModules.SelectMany(mod => mod.Value).ToArray();

            ScannedTypes = scannedModules;
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
}
