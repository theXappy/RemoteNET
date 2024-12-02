using System;
using System.Collections.Concurrent;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Transactions;
using DetoursNet;
using ScubaDiver.API;
using ScubaDiver.API.Hooking;
using ScubaDiver.Rtti;
using TypeInfo = ScubaDiver.Rtti.TypeInfo;

namespace ScubaDiver.Hooking;

public class DetoursNetWrapper
{
    private static DetoursNetWrapper _instance = null;
    public static DetoursNetWrapper Instance => _instance ??= new();

    /// <returns>Skip original</returns>
    public delegate bool HookCallback(DetoursMethodGenerator.DetouredFuncInfo tramp, object[] args, out nuint overridenReturnValue);

    private ConcurrentDictionary<UndecoratedFunction, MethodInfo> _methodsToGenMethods = new ConcurrentDictionary<UndecoratedFunction, MethodInfo>();

    public bool AddHook(TypeInfo typeInfo, UndecoratedFunction methodToHook, HarmonyWrapper.HookCallback realCallback, HarmonyPatchPosition hookPosition)
    {
        if (methodToHook.NumArgs == null)
        {
            // Can't read num of arguments, fuck it.
            return false;
        }
        DetoursMethodGenerator.DetouredFuncInfo tramp;
        Type retType = typeof(nuint);
        if (methodToHook.RetType == "float")
            retType = typeof(float);
        if (methodToHook.RetType == "double")
            retType = typeof(double);
        tramp = DetoursMethodGenerator.GetOrCreateMethod(typeInfo, methodToHook, retType, methodToHook.DecoratedName);
        switch (hookPosition)
        {
            case HarmonyPatchPosition.Prefix:
                if (tramp.PreHook != null)
                    throw new InvalidOperationException(
                        $"Can not set 2 hooks on the same method ({methodToHook.UndecoratedFullName}) in the same position ({hookPosition})");
                tramp.PreHook = realCallback;
                break;
            case HarmonyPatchPosition.Postfix:
                if (tramp.PostHook != null)
                    throw new InvalidOperationException(
                        $"Can not set 2 hooks on the same method ({methodToHook.UndecoratedFullName}) in the same position ({hookPosition})");
                tramp.PostHook = realCallback;
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
        IntPtr targetFunc = new IntPtr(methodToHook.Address);
        success = Loader.HookMethod(module, targetFunc, tramp.DelegateType, tramp.GenerateMethodInfo, tramp.GeneratedDelegate);

        return success;
    }

    public bool RemoveHook(UndecoratedFunction methodToUnhook, HarmonyWrapper.HookCallback callback)
    {
        IntPtr module = new IntPtr((long)methodToUnhook.Module.BaseAddress);
        IntPtr targetFunc = new IntPtr(methodToUnhook.Address);

        if (!_methodsToGenMethods.TryGetValue(methodToUnhook, out MethodInfo genMethodInfo)) 
            return false;

        string key = $"{methodToUnhook.Module.Name}!{methodToUnhook.DecoratedName}";
        if (!DetoursMethodGenerator.TryGetMethod(key,
                out DetoursMethodGenerator.DetouredFuncInfo funcInfo)) 
            return false;

        if (funcInfo.PreHook == callback)
        {
            funcInfo.PreHook = null;
        }
        else if (funcInfo.PostHook == callback)
        {
            funcInfo.PostHook = null;
        }
        else
        {
            throw new ArgumentException(
                $"Trying to unhook a hook that doesn't match neither the pre nor post hooks. Func: {methodToUnhook.UndecoratedName}");
        }

        // Check if the hook is retired
        if (funcInfo.PostHook == null && funcInfo.PreHook == null)
        {
            DetoursMethodGenerator.Remove(key);
            _methodsToGenMethods.TryRemove(methodToUnhook, out _);
            return Loader.UnHookMethod(module, targetFunc, genMethodInfo);
        }
        return true;
    }


}