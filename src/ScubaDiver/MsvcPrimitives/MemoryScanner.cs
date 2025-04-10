﻿using System;
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
using System.Security.Cryptography;
using ScubaDiver.Rtti;

namespace ScubaDiver
{
    public unsafe class MemoryScanner
    {
        public HANDLE _processHandle;
        private bool _is32Bit;

        public MemoryScanner()
        {
            Process process = Process.GetCurrentProcess();
            _processHandle = PInvoke.OpenProcess(PROCESS_ACCESS_RIGHTS.PROCESS_ALL_ACCESS, true, (uint)process.Id);
            if (_processHandle.IsNull) throw new TricksterException();

            BOOL is32Bit;
            PInvoke.IsWow64Process(_processHandle, &is32Bit);
            _is32Bit = is32Bit;
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

        public IDictionary<ulong, IReadOnlyCollection<ulong>> ScanRegions(IEnumerable<nuint> xoredVftables, nuint xorMask = 0x00000000)
        {
            // Get regions
            Logger.Debug("[ScanRegions] Fetching Regions for ScanRegionsCore2");
            Logger.Debug($"[ScanRegions] Fetching Regions for ScanRegionsCore2. xorMask = 0x{xorMask:x16}");
            if (xorMask == 0)
            {
                Logger.Debug("[ScanRegions] XOR MASK is ZEROOOOOOOOOOOOOOO !!!!@@@@@@@@@@@@@@@@@@@@@@@@@@@@@@@@@@@@");
                Logger.Debug("[ScanRegions] XOR MASK is ZEROOOOOOOOOOOOOOO !!!!@@@@@@@@@@@@@@@@@@@@@@@@@@@@@@@@@@@@");
                Logger.Debug("[ScanRegions] XOR MASK is ZEROOOOOOOOOOOOOOO !!!!@@@@@@@@@@@@@@@@@@@@@@@@@@@@@@@@@@@@");
            }
            MemoryRegionInfo[] scannedRegions = ScanRegionInfoCore();
            Logger.Debug($"[ScanRegions] Scanned {scannedRegions.Length} regions INFOs");

            MemoryRegion[] regions = ReadRegionsCore(scannedRegions);
            Logger.Debug($"[ScanRegions] Read {regions.Length} regions' bytes");

            // Scan regions
            IDictionary<ulong, IReadOnlyCollection<ulong>> res = ScanRegionsCore2(regions, xoredVftables, xorMask);
            Logger.Debug($"[ScanRegions] Scanned {res.Count} regions");

            // Free regions
            FreeRegionsCore(regions);

            Logger.Debug("[ScanRegions] Returned from ScanRegionsCore2");
            return res;


            void FreeRegionsCore(MemoryRegion[] regionArray)
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





        /// <summary>
        /// Scan the process memory for vftables to spot instances of First-Class types.
        /// </summary>
        /// <param name="memScanner"></param>
        /// <param name="typeInfos"></param>
        /// <returns></returns>
        public Dictionary<FirstClassTypeInfo, IReadOnlyCollection<ulong>> Scan(IEnumerable<FirstClassTypeInfo> typeInfos)
        {
            Dictionary<nuint, FirstClassTypeInfo> xoredVftableToType = new();
            foreach (FirstClassTypeInfo typeInfo in typeInfos)
            {
                if (xoredVftableToType.ContainsKey(typeInfo.XoredVftableAddress))
                    continue;

                xoredVftableToType[typeInfo.XoredVftableAddress] = typeInfo;
            }

            // Maps xored vftables to instances
            IDictionary<ulong, IReadOnlyCollection<ulong>> xoredVftablesToInstances =
                    ScanRegions(xoredVftableToType.Keys, FirstClassTypeInfo.XorMask);

            Dictionary<FirstClassTypeInfo, IReadOnlyCollection<ulong>> res = new();
            foreach (var kvp in xoredVftablesToInstances)
            {
                res[xoredVftableToType[(nuint)kvp.Key]] = kvp.Value;
            }

            return res;
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
}
