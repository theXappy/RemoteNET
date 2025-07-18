using System;
using System.Collections.Concurrent;
using System.Reflection;
using DetoursNet;
using ScubaDiver.API.Hooking;
using TypeInfo = ScubaDiver.Rtti.TypeInfo;

namespace ScubaDiver.Hooking;

public class DetoursNetWrapper
{
    private static DetoursNetWrapper _instance = null;
    public static DetoursNetWrapper Instance => _instance ??= new();

    private ConcurrentDictionary<UndecoratedFunction, MethodInfo> _methodsToGenMethods = new ConcurrentDictionary<UndecoratedFunction, MethodInfo>();

    public bool AddHook(TypeInfo typeInfo, UndecoratedFunction methodToHook, HarmonyWrapper.HookCallback realCallback, HarmonyPatchPosition hookPosition)
    {
        if (methodToHook.NumArgs == null)
        {
            // Can't read num of arguments, fuck it.
            return false;
        }

        DetoursMethodGenerator.DetouredFuncInfo tramp = DetoursMethodGenerator.GetOrCreateMethod(typeInfo, methodToHook, methodToHook.DecoratedName);
        switch (hookPosition)
        {
            case HarmonyPatchPosition.Prefix:
                tramp.PreHooks.Add(realCallback);
                break;
            case HarmonyPatchPosition.Postfix:
                tramp.PostHooks.Add(realCallback);
                break;
            case HarmonyPatchPosition.Finalizer:
            default:
                throw new ArgumentOutOfRangeException(nameof(hookPosition), hookPosition, null);
        }

        if (_methodsToGenMethods.TryGetValue(methodToHook, out var existingMi))
        {
            if (existingMi != tramp.GenerateMethodInfo)
            {
                throw new Exception(
                    "DetoursNetWrapper: Mismatch between stored generated MethodInfo and newly created generated MethodInfo");
            }

            // Method was already hooked and with the rgith Generated Method Info
            // We just updated the pre/post properties
            // So nothing else to do here!
            return true;
        }

        // Method not *actually* hooked yet.
        // Keep memo aside about it
        _methodsToGenMethods[methodToHook] = tramp.GenerateMethodInfo;


        // First, try to hook with module name + export name (won't work for internal methods)
        bool success = Loader.HookMethod(typeInfo.ModuleName, methodToHook.DecoratedName,
            tramp.DelegateType,
            tramp.GenerateMethodInfo,
            tramp.GeneratedDelegate);
        if (success)
            return true;

        // Fallback, Try directly with pointers
        Console.WriteLine($"[DetoursNetWrapper] Hooking with LoadLibrary + GetProcAddress failed, trying direct pointers. Target: {methodToHook.Module.Name}!{methodToHook.UndecoratedFullName}");
        IntPtr module = new IntPtr((long)methodToHook.Module.BaseAddress);
        IntPtr targetFunc = new IntPtr((long)methodToHook.Address);
        success = Loader.HookMethod(module, targetFunc, tramp.DelegateType, tramp.GenerateMethodInfo, tramp.GeneratedDelegate);

        return success;
    }

    public bool RemoveHook(UndecoratedFunction methodToUnhook, HarmonyWrapper.HookCallback callback)
    {
        IntPtr module = new IntPtr((long)methodToUnhook.Module.BaseAddress);
        IntPtr targetFunc = new IntPtr((long)methodToUnhook.Address);

        if (!_methodsToGenMethods.TryGetValue(methodToUnhook, out MethodInfo genMethodInfo)) 
            return false;

        string key = $"{methodToUnhook.Module.Name}!{methodToUnhook.DecoratedName}";
        if (!DetoursMethodGenerator.TryGetMethod(key,
                out DetoursMethodGenerator.DetouredFuncInfo funcInfo)) 
            return false;

        bool removed = funcInfo.PreHooks.Remove(callback);
        removed = removed || funcInfo.PostHooks.Remove(callback);
        if (!removed)
        {
            throw new ArgumentException(
                $"Trying to unhook a hook that doesn't match neither the pre nor post hooks. Func: {methodToUnhook.UndecoratedName}");
        }

        // Check if the hook is retired
        if (funcInfo.PostHooks.Count == 0 && funcInfo.PreHooks.Count == 0)
        {
            DetoursMethodGenerator.Remove(key);
            _methodsToGenMethods.TryRemove(methodToUnhook, out _);
            return Loader.UnHookMethod(module, targetFunc, genMethodInfo);
        }
        return true;
    }


}