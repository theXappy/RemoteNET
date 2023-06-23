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

    private void RemoveFromDicts(string decoratedTargetName, HookCallback callback)
    {
        if (_hooksToGenMethod.TryGetValue(decoratedTargetName, out var hooksDict))
        {
            hooksDict.TryRemove(callback, out MethodInfo generateMethodInfo);
            _actualHooks.TryRemove(generateMethodInfo, out _);
        }
    }

    public bool AddHook(UndecoratedFunction target, HookCallback callback)
    {
        if (target.NumArgs == null)
        {
            // Can't read num of arguments, fuck it.
            return false;
        }
        // TODO: Is "nuint" return type always right here?
        var tramp = DetoursMethodGenerator.GenerateMethod(target.NumArgs.Value, typeof(nuint), target.UndecoratedName, SingeCallback);

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
        Console.WriteLine($"[DetoursNetWrapper] Hooking with LoadLibrary + GetProcAddress failed, trying direct pointers. Target: {target.Module.Name}!{target.UndecoratedName}");
        IntPtr module = new IntPtr((long)target.Module.BaseAddress);
        IntPtr targetFunc = new IntPtr(target.Address);
        success = Loader.HookMethod(module, targetFunc, tramp.DelegateType, tramp.GenerateMethodInfo, tramp.GeneratedDelegate);

        return success;
    }

    public void RemoveHook(UndecoratedFunction target, HookCallback callback)
    {
        RemoveFromDicts(target.DecoratedName, callback);
    }

    private static nuint SingeCallback(DetoursMethodGenerator.DetoursTrampoline tramp, object[] args)
    {
        //Console.WriteLine($"[SingleCallback] Invoked for {tramp.Name} with {args.Length} arguments");

        // Call hook
        var hook = _actualHooks[tramp.GenerateMethodInfo];
        bool callOriginal = hook(tramp, args, out nuint overridenReturnValue);

        if (!callOriginal)
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
        return (nuint)0x0BBB_BBBB_BBAD_C0DE;
    }
}