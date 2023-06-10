using System;
using System.Collections.Concurrent;
using System.IO;
using System.Reflection;
using ScubaDiver.API.Hooking;

namespace ScubaDiver.Hooking;

public class DetoursNetWrapper
{
    private static DetoursNetWrapper _instance = null;
    public static DetoursNetWrapper Instance => _instance ??= new();


    private static readonly ConcurrentDictionary<string, HarmonyWrapper.HookCallback> _actualHooks = new();

    public bool AddHook(UndecoratedFunction target, HarmonyPatchPosition pos, Type delegateType, MethodInfo mi, Delegate delegateValue = null)
    {
        string moduleName = Path.GetFileNameWithoutExtension(target.Module.Name);

        // First try to hook with module name + export name (won't work for internal methods)
        bool success = DetoursNet.Loader.HookMethod(moduleName, target.DecoratedName, delegateType, mi, delegateValue);
        if (success)
            return true;

        Console.WriteLine($"[DetoursNetWrapper] Hooking with LoadLibrary + GetProcAddress failed, trying direct pointers. Target: {target.Module.Name}!{target.UndecoratedName}");
        // Fallback, Try directly with pointers
        IntPtr module = new IntPtr((long)target.Module.BaseAddress);
        IntPtr targetFunc = new IntPtr(target.Address);
        success = DetoursNet.Loader.HookMethod(module, targetFunc, delegateType, mi, delegateValue);

        return success;
    }
}