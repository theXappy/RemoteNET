using System;
using System.Reflection;
using System.Text;
using ScubaDiver.API.Memory;

namespace RemoteNET;

public class RemoteMarshal
{
    private readonly ManagedRemoteApp _app;
    private readonly Type _remoteMarshalType;
    private readonly MethodInfo _remoteAlloc;
    private readonly MethodInfo _remoteAllocZero;
    private readonly MethodInfo _remoteFree;
    private readonly MethodInfo _remoteWrite;
    private readonly MethodInfo _remoteRead;
    private readonly MethodInfo _remoteMemSetZero;
    private readonly MethodInfo _remotePtrToStringAnsi;

    public RemoteMarshal(ManagedRemoteApp app)
    {
        _app = app;
        _remoteMarshalType = _app.GetRemoteType(typeof(SafeMarshal));
        _remoteAlloc = _remoteMarshalType.GetMethod(nameof(SafeMarshal.AllocHGlobal), (BindingFlags)0xffff, new[] { typeof(int) });
        _remoteAllocZero = _remoteMarshalType.GetMethod(nameof(SafeMarshal.AllocHGlobalZero), (BindingFlags)0xffff, new[] { typeof(int) });
        _remoteFree = _remoteMarshalType.GetMethod(nameof(SafeMarshal.FreeHGlobal), (BindingFlags)0xffff, new[] { typeof(IntPtr) });
        _remoteWrite = _remoteMarshalType.GetMethod(nameof(SafeMarshal.Copy), (BindingFlags)0xffff, new[] { typeof(byte[]), typeof(int), typeof(IntPtr), typeof(int) });
        _remoteRead = _remoteMarshalType.GetMethod(nameof(SafeMarshal.Copy), (BindingFlags)0xffff, new[] { typeof(IntPtr), typeof(byte[]), typeof(int), typeof(int) });
        _remoteMemSetZero = _remoteMarshalType.GetMethod(nameof(SafeMarshal.MemSetZero), (BindingFlags)0xffff, new[] { typeof(byte[]) });
        _remotePtrToStringAnsi = _remoteMarshalType.GetMethod(nameof(SafeMarshal.PtrToStringAnsi), (BindingFlags)0xffff, new[] { typeof(IntPtr) });
    }

    public IntPtr AllocHGlobal(int cb)
    {
        object results = _remoteAlloc.Invoke(obj: null, [cb]);
        return (IntPtr)results;
    }

    public IntPtr AllocHGlobalZero(int cb)
    {
        object results = _remoteAllocZero.Invoke(obj: null, [cb]);
        return (IntPtr)results;
    }

    public void FreeHGlobal(IntPtr hglobal)
    {
        _remoteFree.Invoke(obj: null, [hglobal]);
    }

    public void Copy(byte[] source, int startIndex, IntPtr destination, int length)
    {
        // The byte array is encoded entirely and sent to the diver.
        _remoteWrite.Invoke(obj: null, [source, startIndex, destination, length]);
    }

    public void Write(byte[] source, int startIndex, IntPtr destination, int length)
        => Copy(source, startIndex, destination, length);

    public void Copy(IntPtr source, byte[] destination, int startIndex, int length)
    {
        // We use a temporary array in the target because the Copy method modifies "destination" but out "Invoke" flow
        // only makes a copy of our local one, so the changes to the remote one aren't reflected back.
        var remoteArray = _app.Activator.CreateInstance(typeof(byte[]), length);
        _remoteRead.Invoke(obj: null, [source, remoteArray, 0, length]);

        // Casting to byte[] causes a copy to the local process
        var dro = remoteArray.Dynamify();
        byte[] copiedLocal = (byte[])dro;

        // Now destroy the remote array since we don't want the heap scan to mistakenly 
        // find any vftables of objects within it.
        _remoteMemSetZero.Invoke(obj: null, [remoteArray]);

        Array.Copy(copiedLocal, 0, destination, startIndex, length);
    }

    public void Read(IntPtr source, byte[] destination, int startIndex, int length)
        => Copy(source, destination, startIndex, length);

    public byte[] Read(IntPtr source, int length)
    {
        byte[] output = new byte[length];
        Read(source, output, 0, length);
        return output;
    }

    public string PtrToStringAnsi(IntPtr ptr)
    {
        if(ptr == IntPtr.Zero)
            return null;

        return _remotePtrToStringAnsi.Invoke(null, [ptr]) as string;
    }


    public nuint CreateRemoteString(string s, Encoding encoding = null)
    {
        encoding ??= Encoding.ASCII;

        // Allocate memory for the string, assuming ASCII
        byte[] encodedBytes = encoding.GetBytes(s + '\x00');
        nint remoteBuf = AllocHGlobal(encodedBytes.Length);
        // Copy the string to the remote buffer
        Write(encodedBytes, 0, remoteBuf, encodedBytes.Length);
        return (nuint)remoteBuf;
    }

    public ulong ReadQword(ulong address)
    {
        // Read a 64-bit value from the remote process
        byte[] buffer = Read((nint)address, 8);
        if (buffer.Length < 8)
            throw new Exception("Failed to read 64-bit value");
        return (nuint)BitConverter.ToUInt64(buffer, 0);
    }

    public ulong ReadQword(UIntPtr address) => ReadQword((ulong)address);

    public void WriteQword(ulong address, ulong value)
    {
        // Convert the 64-bit value to a byte array
        byte[] buffer = BitConverter.GetBytes(value);
        // Write the byte array to the remote process
        Write(buffer, 0, (nint)address, buffer.Length);
    }
    public void WriteQword(UIntPtr address, ulong value) => WriteQword((ulong)address, value);

}