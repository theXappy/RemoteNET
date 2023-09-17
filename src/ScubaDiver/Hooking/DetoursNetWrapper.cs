using System;
using System.Collections.Concurrent;
using System.IO;
using System.Reflection;
using System.Transactions;
using DetoursNet;

namespace ScubaDiver.Hooking;

public class DetoursNetWrapper
{
    private static DetoursNetWrapper _instance = null;
    public static DetoursNetWrapper Instance => _instance ??= new();

    /// <returns>Skip original</returns>
    public delegate bool HookCallback(DetoursMethodGenerator.DetoursTrampoline tramp, object[] args, out nuint overridenReturnValue);

    private static readonly ConcurrentDictionary<MethodInfo, HookCallback> _actualHooks = new();
    private static readonly ConcurrentDictionary<string, ConcurrentDictionary<HookCallback, MethodInfo>> _hooksToGenMethod = new();

    private void AddToDicts(string decoratedTargetName, HookCallback callback, MethodInfo generateMethodInfo)
    {
        _actualHooks[generateMethodInfo] = callback;
        if (!_hooksToGenMethod.TryGetValue(decoratedTargetName, out var hooksDict))
        {
            hooksDict = new ConcurrentDictionary<HookCallback, MethodInfo>();
            _hooksToGenMethod[decoratedTargetName] = hooksDict;
        }
        hooksDict[callback] = generateMethodInfo;
    }

    private bool RemoveFromDicts(string decoratedTargetName, HookCallback callback, out MethodInfo generateMethodInfo)
    {
        generateMethodInfo = null;
        bool res = false;
        if (_hooksToGenMethod.TryGetValue(decoratedTargetName, out var hooksDict))
        {
            res = hooksDict.TryRemove(callback, out generateMethodInfo) || 
                  _actualHooks.TryRemove(generateMethodInfo, out _);
        }

        return res;
    }

    public bool AddHook(UndecoratedFunction target, HookCallback callback)
    {
        if (target.NumArgs == null)
        {
            // Can't read num of arguments, fuck it.
            return false;
        }
        // TODO: Is "nuint" return type always right here?
        var tramp = DetoursMethodGenerator.GenerateMethod(target, typeof(nuint), target.DecoratedName, SingeCallback);

        AddToDicts(target.DecoratedName, callback, tramp.GenerateMethodInfo);

        // First, try to hook with module name + export name (won't work for internal methods)
        string moduleName = Path.GetFileNameWithoutExtension(target.Module.Name);
        bool success = Loader.HookMethod(moduleName, target.DecoratedName,
            tramp.DelegateType,
            tramp.GenerateMethodInfo,
            tramp.GeneratedDelegate);
        if (success)
            return true;

        // Fallback, Try directly with pointers
        Console.WriteLine($"[DetoursNetWrapper] Hooking with LoadLibrary + GetProcAddress failed, trying direct pointers. Target: {target.Module.Name}!{target.UndecoratedFullName}");
        IntPtr module = new IntPtr((long)target.Module.BaseAddress);
        IntPtr targetFunc = new IntPtr(target.Address);
        success = Loader.HookMethod(module, targetFunc, tramp.DelegateType, tramp.GenerateMethodInfo, tramp.GeneratedDelegate);

        return success;
    }

    public bool RemoveHook(UndecoratedFunction target, HookCallback callback)
    {
        IntPtr module = new IntPtr((long)target.Module.BaseAddress);
        IntPtr targetFunc = new IntPtr(target.Address);

        bool success = false;
        if (RemoveFromDicts(target.DecoratedName, callback, out MethodInfo genMethodInfo))
        {
            success = Loader.UnHookMethod(module, targetFunc, genMethodInfo);
        }
        return success;
    }

    private static nuint SingeCallback(DetoursMethodGenerator.DetoursTrampoline tramp, object[] args)
    {
        //Console.WriteLine($"[SingleCallback] Invoked for {tramp.Name} with {args.Length} arguments");

        // Call hook
        var hook = _actualHooks[tramp.GenerateMethodInfo];
        bool skipOriginal = hook(tramp, args, out nuint overridenReturnValue);

        if (skipOriginal)
        {
            return overridenReturnValue;
        }

        // Call original method
        Delegate realMethod = DelegateStore.Real[tramp.GenerateMethodInfo];
        object res = realMethod.DynamicInvoke(args);
        //Console.WriteLine($"[SingleCallback] Original of {tramp.Name} returned {res}");
        if (res != null)
            return (nuint)res;

        // Could be a void-returning method. I hope this doesn't break anything.
        return (nuint)0xBBAD_C0DE;
    }
}