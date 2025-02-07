using NtApiDotNet;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using Windows.Win32;
using Windows.Win32.Foundation;

namespace ScubaDiver.Rtti;

public unsafe struct RttiScanner : IDisposable
{
    private nuint _baseAddress;
    private nuint _size;
    private byte* _pointer;

    public RttiScanner(HANDLE handle, nuint mainModuleBaseAddress, nuint mainModuleSize, List<ModuleSegment> segments)
    {
        _baseAddress = mainModuleBaseAddress;
        _size = mainModuleSize;
        _pointer = (byte*)NativeMemory.Alloc(mainModuleSize);
        nuint numberOfBytesRead = 0;
        nuint* lpNumberOfBytesRead = &numberOfBytesRead;
        {
            foreach (ModuleSegment segment in segments)
            {
                ulong distance = segment.BaseAddress - mainModuleBaseAddress;
                if (!PInvoke.ReadProcessMemory(handle, (void*)segment.BaseAddress,
                        _pointer + distance,
                        (nuint)segment.Size,
                        lpNumberOfBytesRead))
                {
                    int gle = Marshal.GetLastWin32Error();
                    string error = $"RttiScanner failed on ReadProcessMemory(" +
                                   $"hProcess: 0x{handle:x16}," +
                                   $"lpBaseAddress: 0x{mainModuleBaseAddress:x16}," +
                                   $"lpBuffer: 0x{(nuint)_pointer:x16}," +
                                   $"nSize: 0x{mainModuleSize:x16}," +
                                   $"lpNumberOfBytesRead: 0x{new IntPtr(lpNumberOfBytesRead):x16}" +
                                   $")\n" +
                                   $"numberOfBytesRead was: 0x{numberOfBytesRead:x16}\n" +
                                   $"GetLastError: 0x{gle:x16}";

                    //Logger.Debug($"[RTTI Scanner] Error reading segment {segment.Name} in module @0x{mainModuleBaseAddress}. Formatter error:\n" + error);
                    throw new ApplicationException(error);
                }
                else
                {
                }
            }

        }
    }

    public void Dispose()
    {
        NativeMemory.Free(_pointer);
    }

    public unsafe bool TryRead(ulong address, int count, void* buffer)
    {
        if (address >= _baseAddress && address + (uint)count < _baseAddress + _size && (ulong.MaxValue - (uint)count > address))
        {
            void* sourceAddress = _pointer + (address - _baseAddress);
            Unsafe.CopyBlock(buffer, sourceAddress, (uint)count);
            return true;
        }
        return false;
    }

    public bool TryRead(ulong address, out ulong result)
    {
        fixed (void* ptr = &result)
            return TryRead(address, sizeof(ulong), ptr);
    }
    public bool TryRead(ulong address, out nuint result)
    {
        fixed (void* ptr = &result)
            return TryRead(address, sizeof(nuint), ptr);
    }
    public bool TryRead(ulong address, out uint result)
    {
        fixed (void* ptr = &result)
            return TryRead(address, sizeof(uint), ptr);
    }

    public bool TryRead<T>(ulong address, Span<T> buffer) where T : unmanaged
    {
        fixed (void* ptr = buffer)
            return TryRead(address, buffer.Length * sizeof(T), ptr);
    }

    private const int BUFFER_SIZE = 256;

    public string GetClassName64(ulong address, List<ModuleSegment> segments)
    {
        if (!TryRead(address - 0x08, out ulong object_locator)) return null;

        // January 2025: Trying to optimize this
        //bool IsInSegment(ModuleSegment segment) => object_locator >= segment.BaseAddress && object_locator <= segment.BaseAddress + segment.Size;
        //bool isInOutOfTheSegment = segments.Any(IsInSegment);
        //if (!isInOutOfTheSegment)
        //    return null;

        if (!TryRead(object_locator + 0x14, out ulong base_offset)) return null;
        ulong base_address = object_locator - base_offset;
        if (!TryRead(object_locator + 0x0C, out uint type_descriptor_offset)) return null;
        ulong class_name = base_address + type_descriptor_offset + 0x10 + 0x04;
        byte* buffer = stackalloc byte[BUFFER_SIZE];
        buffer[0] = (byte)'?';
        if (!TryRead(class_name, BUFFER_SIZE - 1, buffer + 1)) return null;
        return UnDecorateSymbolNameWrapper(buffer);
    }

    private static object _dbgHelpLock = new object();
    public static string UnDecorateSymbolNameWrapper(byte* buffer)
    {
        lock (_dbgHelpLock)
        {
            byte* target = stackalloc byte[BUFFER_SIZE];
            uint len = PInvoke.UnDecorateSymbolName(new PCSTR(buffer), new PSTR(target), BUFFER_SIZE, 0x1800);
            return len != 0 ? Encoding.UTF8.GetString(target, (int)len) : null;
        }
    }
    public static string UnDecorateSymbolNameWrapper(string buffer)
    {
        lock (_dbgHelpLock)
        {
            byte* target = stackalloc byte[BUFFER_SIZE];
            uint len = PInvoke.UnDecorateSymbolName(buffer, new PSTR(target), BUFFER_SIZE, 0x1800);
            return len != 0 ? Encoding.UTF8.GetString(target, (int)len) : null;
        }
    }

    public string GetClassName32(ulong address, List<ModuleSegment> segments)
    {
        if (!TryRead(address - 0x04, out uint object_locator)) return null;
        if (!TryRead(object_locator + 0x06, out uint type_descriptor)) return null;
        ulong class_name = type_descriptor + 0x0C + 0x03;
        byte* buffer = stackalloc byte[BUFFER_SIZE];
        buffer[0] = (byte)'?';
        if (!TryRead(class_name, BUFFER_SIZE - 1, buffer + 1)) return null;
        lock (_dbgHelpLock)
        {
            byte* target = stackalloc byte[BUFFER_SIZE];
            uint len = PInvoke.UnDecorateSymbolName(new PCSTR(buffer), new PSTR(target), BUFFER_SIZE, 0x1000);
            return len != 0 ? Encoding.UTF8.GetString(target, (int)len) : null;
        }
    }
}