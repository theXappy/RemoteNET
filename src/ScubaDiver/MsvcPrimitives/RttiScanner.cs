using NtApiDotNet;
using System;
using System.Diagnostics;
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

    public RttiScanner(HANDLE handle, nuint mainModuleBaseAddress, nuint mainModuleSize)
    {
        _baseAddress = mainModuleBaseAddress;
        _size = mainModuleSize;
        _pointer = (byte*)NativeMemory.Alloc(mainModuleSize);
        if (!PInvoke.ReadProcessMemory(handle, (void*)mainModuleBaseAddress, _pointer, mainModuleSize))
        {
            throw new ApplicationException("RttiScanner failed on ReadProcessMemory");
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

    public bool TryRead<T>(ulong address, out T result) where T : unmanaged
    {
        fixed (void* ptr = &result)
            return TryRead(address, sizeof(T), ptr);
    }
    public bool TryRead<T>(ulong address, Span<T> buffer) where T : unmanaged
    {
        fixed (void* ptr = buffer)
            return TryRead(address, buffer.Length * sizeof(T), ptr);
    }

    private const int BUFFER_SIZE = 256;

    public string GetClassName64(ulong address)
    {
        if (!TryRead(address - 0x08, out ulong object_locator)) return null;
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

    public string GetClassName32(ulong address)
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