using System;
using System.Reflection;
using System.Runtime.InteropServices;
using ScubaDiver.API.Memory;
using RemoteNET.Extensions;
using System.Linq;
using System.Net.Mime;

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
    private readonly MethodInfo _remotePtrToStringAnsi;

    private readonly MethodInfo _toBase64;

    public RemoteMarshal(ManagedRemoteApp app)
    {
        _app = app;
        _remoteMarshalType = _app.GetRemoteType(typeof(SafeMarshal));
        _remoteAlloc = _remoteMarshalType.GetMethod(nameof(SafeMarshal.AllocHGlobal), (BindingFlags)0xffff, new[] { typeof(int) });
        _remoteAllocZero = _remoteMarshalType.GetMethod(nameof(SafeMarshal.AllocHGlobalZero), (BindingFlags)0xffff, new[] { typeof(int) });
        _remoteFree = _remoteMarshalType.GetMethod(nameof(SafeMarshal.FreeHGlobal), (BindingFlags)0xffff, new[] { typeof(IntPtr) });
        _remoteWrite = _remoteMarshalType.GetMethod(nameof(SafeMarshal.Copy), (BindingFlags)0xffff, new[] { typeof(byte[]), typeof(int), typeof(IntPtr), typeof(int) });
        _remoteRead = _remoteMarshalType.GetMethod(nameof(SafeMarshal.Copy), (BindingFlags)0xffff, new[] { typeof(IntPtr), typeof(byte[]), typeof(int), typeof(int) });
        _remotePtrToStringAnsi = _remoteMarshalType.GetMethod(nameof(SafeMarshal.PtrToStringAnsi), (BindingFlags)0xffff, new[] { typeof(IntPtr) });

        Type ConvertType = _app.GetRemoteType(typeof(Convert));
        _toBase64 = ConvertType.GetMethods().Single(mi => mi.Name == nameof(Convert.ToBase64String) && mi.GetParameters().Length == 1);
    }

    public IntPtr AllocHGlobal(int cb)
    {
        object results = _remoteAlloc.Invoke(obj: null, new object[1] { cb });
        return (IntPtr)results;
    }

    public IntPtr AllocHGlobalZero(int cb)
    {
        object results = _remoteAllocZero.Invoke(obj: null, new object[1] { cb });
        return (IntPtr)results;
    }

    public void FreeHGlobal(IntPtr hglobal)
    {
        _remoteFree.Invoke(obj: null, new object[1] { hglobal });
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
        _remoteRead.Invoke(obj: null, new object[4] { source, remoteArray, 0, length });

        // Ugly cast-to-string remotely, then fetch and convert back to byte[].
        byte[] copiedLocal = __cast_to_byte_array(remoteArray);

        Array.Copy(copiedLocal, 0, destination, startIndex, length);
        return;

        byte[] __cast_to_byte_array(RemoteObject ro)
        {
            // Invoke remote Convert.ToBase64String(byte[])
            string base64 = _toBase64.Invoke(null, [ro]) as string;
            return Convert.FromBase64String(base64);
        }
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
        if (ptr == IntPtr.Zero)
            return null;

        return _remotePtrToStringAnsi.Invoke(null, new object[1] { ptr }) as string;
    }
}