using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using NtApiDotNet.Win32;
using Windows.Win32;
using Windows.Win32.Foundation;
using System.Runtime.InteropServices;
using System;

namespace ScubaDiver;

public class ExportsMaster : IReadOnlyExportsMaster
{
    private Dictionary<string, List<DllExport>> _exportsCache = new();
    private Dictionary<string, List<DllImport>> _importsCache = new();


    // Inner `int` key is the ORDINAL
    private Dictionary<Rtti.ModuleInfo, Dictionary<int, UndecoratedSymbol>> _undecExportsCache = new();
    private Dictionary<Rtti.ModuleInfo, List<DllExport>> _leftoverExportsCache = new();

    private SortedDictionary<nuint, Rtti.ModuleInfo> _moduleInfoByAddress = new();


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

    public ICollection<UndecoratedSymbol> GetUndecoratedExports(Rtti.ModuleInfo modInfo)
    {
        ProcessExports(modInfo);
        return _undecExportsCache[modInfo].Values;
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
            if (export.TryUndecorate(modInfo, out UndecoratedSymbol undecExp))
                undecoratedExports.Add(undecExp);
            else
                leftoverExports.Add(export);
        }
        _undecExportsCache[modInfo] = undecoratedExports.ToDictionary(keySelector: GetOrdinal);
        _leftoverExportsCache[modInfo] = leftoverExports.ToList();

        if (!_moduleInfoByAddress.ContainsKey(modInfo.BaseAddress))
        {
            _moduleInfoByAddress.Add(modInfo.BaseAddress, modInfo);
        }

        return;

        int GetOrdinal(UndecoratedSymbol undecSym)
        {
            if (undecSym is UndecoratedExportedFunc undecFunc)
                return undecFunc.Export.Ordinal;
            else if (undecSym is UndecoratedExportedField undecField)
                return undecField.Export.Ordinal;
            else
                throw new InvalidOperationException("Unknown export type");
        }
    }

    /// <summary>
    /// Get a specific type from a specific module.
    /// </summary>
    public IEnumerable<UndecoratedSymbol> GetExportedTypeMembers(Rtti.ModuleInfo module, string typeFullName)
    {
        string membersPrefix = $"{typeFullName}::";
        ProcessExports(module);
        return _undecExportsCache[module].Values.Where(sym => sym.UndecoratedFullName.StartsWith(membersPrefix));
    }
    public IEnumerable<UndecoratedFunction> GetExportedTypeFunctions(Rtti.ModuleInfo module, string typeFullName)
    {
        return GetExportedTypeMembers(module, typeFullName).OfType<UndecoratedFunction>();
    }

    private unsafe bool TryReadMemory(nuint address, nuint size, out byte[]? buffer)
    {
        buffer = null;

        // Get the current process handle
        Process process = Process.GetCurrentProcess();
        HANDLE processHandle = PInvoke.OpenProcess(Windows.Win32.System.Threading.PROCESS_ACCESS_RIGHTS.PROCESS_ALL_ACCESS, true, (uint)process.Id);
        if (processHandle.IsNull)
        {
            // Handle error: Unable to open process
            return false;
        }

        // Allocate memory to read into
        void* memoryPtr = NativeMemory.AllocZeroed(size, 1);
        if (memoryPtr == null)
        {
            PInvoke.CloseHandle(processHandle);
            return false;
        }

        // Read the memory
        if (PInvoke.ReadProcessMemory(processHandle, (void*)address, memoryPtr, size) == false)
        {
            // Handle error: Unable to read memory
            NativeMemory.Free(memoryPtr);
            PInvoke.CloseHandle(processHandle);
            return false;
        }

        // Copy the memory to a byte array
        buffer = new byte[size];
        Marshal.Copy((IntPtr)memoryPtr, buffer, 0, (int)size);

        // Free the allocated memory and close the handle
        NativeMemory.Free(memoryPtr);
        PInvoke.CloseHandle(processHandle);

        return true;
    }

    private Rtti.ModuleInfo? FindContainingModule(nuint xoredAddress)
    {
        // Use binary search to find the module
        var keys = _moduleInfoByAddress.Keys.ToList();
        int index = keys.BinarySearch(xoredAddress ^ UndecoratedExportedField.XorMask);
        if (index < 0)
        {
            // If the address is not found, BinarySearch returns the bitwise complement of the index of the next larger element
            index = ~index - 1;
        }

        if (index < 0 || index >= keys.Count)
            return null;

        Rtti.ModuleInfo module = _moduleInfoByAddress[keys[index]];
        if ((xoredAddress ^ UndecoratedExportedField.XorMask) >= module.BaseAddress + module.Size)
            return null;

        return module;
    }

    public unsafe UndecoratedSymbol? QueryExportByAddress(nuint address)
    {
        // Determine pointer size (4 bytes for 32-bit, 8 bytes for 64-bit)
        int ptrSize = IntPtr.Size;

        // Read the value at the given address
        byte[]? valueBuffer = null;
        if (!TryReadMemory(address, (nuint)ptrSize, out valueBuffer) || valueBuffer == null)
            return null;
        nuint xoredValueAtAddress = UndecoratedExportedField.XorMask ^ (ptrSize == 4 ?
            (nuint)BitConverter.ToUInt32(valueBuffer, 0) :
            (nuint)BitConverter.ToUInt64(valueBuffer, 0));

        // Destroy pattern in the buffer
        for (int i = 1; i < valueBuffer.Length; i++)
            valueBuffer[i] = (byte)(valueBuffer[i-1] ^ 0xCC);

        // Read the ordinal at address + ptrSize
        byte[]? ordinalBuffer = null;
        if (!TryReadMemory(address + (nuint)ptrSize, sizeof(uint), out ordinalBuffer) || ordinalBuffer == null)
            return null;
        uint ordinal = BitConverter.ToUInt32(ordinalBuffer, 0);

        Rtti.ModuleInfo? module = FindContainingModule(xoredValueAtAddress);
        if (module == null)
            return null;

        if (!_undecExportsCache.TryGetValue(module.Value, out Dictionary<int, UndecoratedSymbol> ordinalsDict))
            return null;

        if (!ordinalsDict.TryGetValue((int)ordinal, out UndecoratedSymbol export))
            return null;

        if (export.XoredAddress != xoredValueAtAddress)
            return null;

        return export;
    }
}
