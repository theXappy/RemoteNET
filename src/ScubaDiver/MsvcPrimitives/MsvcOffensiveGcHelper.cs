using System;
using System.Runtime.InteropServices;

public static class MsvcOffensiveGcHelper
{
    // Import the method to add an address to the tracking list
    [DllImport("MsvcOffensiveGcHelper.dll", EntryPoint = "?AddAddress@@YAXPEAX@Z", CallingConvention = CallingConvention.Cdecl)]
    public static extern void AddAddress(IntPtr address);

    // Import the method to remove an address from the tracking list
    [DllImport("MsvcOffensiveGcHelper.dll", EntryPoint = "?RemoveAddress@@YAXPEAX@Z", CallingConvention = CallingConvention.Cdecl)]
    public static extern void RemoveAddress(IntPtr address);

    // Import the AllocateHookForModule function from the DLL
    [DllImport("MsvcOffensiveGcHelper.dll", EntryPoint = "?AllocateHookForModule@@YAHPEAX@Z", CallingConvention = CallingConvention.Cdecl)]
    public static extern int AllocateHookForModule(IntPtr originalFreeFunction);

    // Load the DLL once and cache the handle
    private static Windows.Win32.FreeLibrarySafeHandle hModule = Windows.Win32.PInvoke.LoadLibrary("MsvcOffensiveGcHelper.dll");

    // Method to get the function address of a specific hook
    public static IntPtr GetFuncAddress(int index)
    {
        if (hModule.IsInvalid)
        {
            throw new Exception("Failed to load DLL.");
        }

        Windows.Win32.Foundation.FARPROC funcAddress = Windows.Win32.PInvoke.GetProcAddress(hModule, $"?HookForFree{index}@@YAXPEAX0@Z");
        if (funcAddress.IsNull)
        {
            throw new Exception($"Failed to get address of index = {index}.");
        }

        return funcAddress;
    }

    /// <summary>
    /// Get a hook function for a specific `free`/`_free`/`_free_dbg` method.
    /// Returned function is guaranteed to have the same signature as `free` and call the argument one when needed
    /// </summary>
    public static IntPtr GetOrAddReplacement(IntPtr freeFuncPtr)
    {
        int hookIndex = AllocateHookForModule(freeFuncPtr);
        if (hookIndex < 0)
        {
            throw new Exception("No available slot for hooking free.");
        }
        return GetFuncAddress(hookIndex);
    }
}
