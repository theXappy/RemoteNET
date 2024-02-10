using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.InteropServices;
using System.Xml.Linq;
using DetoursNet;
using ScubaDiver.API;
using ScubaDiver.API.Hooking;
using ScubaDiver.Hooking;
using ScubaDiver.Rtti;
using TypeInfo = ScubaDiver.Rtti.TypeInfo;

namespace ScubaDiver;

public static class DetoursMethodGenerator
{
    public class DetouredFuncInfo
    {
        public TypeInfo DeclaringClass { get; set; }
        public HarmonyWrapper.HookCallback PreHook { get; set; }
        public HarmonyWrapper.HookCallback PostHook { get; set; }

        public UndecoratedFunction Target { get; set; }
        public MethodInfo GenerateMethodInfo { get; set; }
        public Delegate GeneratedDelegate { get; set; }
        public Type DelegateType { get; set; }
        public string Name { get; set; }
        public DetouredFuncInfo(
            TypeInfo declaringClass, UndecoratedFunction target,
            string name,
            MethodInfo generateMethodInfo, Delegate generatedDelegate, Type delegateType)
        {
            DeclaringClass = declaringClass;
            Target = target;
            GenerateMethodInfo = generateMethodInfo;
            GeneratedDelegate = generatedDelegate;
            DelegateType = delegateType;
            Name = name;
        }

        public T GetRealMethod<T>() where T : Delegate
        {
            // Ugly casting
            Delegate originalDelegate = DelegateStore.GetReal(GenerateMethodInfo);
            IntPtr functionPointer = Marshal.GetFunctionPointerForDelegate(originalDelegate);
            T output = (T)Marshal.GetDelegateForFunctionPointer(functionPointer, typeof(T));
            return output;
        }
    }

    private static Dictionary<string, DetouredFuncInfo> _trampolines = new();

    public delegate nuint DetoursWrapperCallback(DetouredFuncInfo tramp, object[] args);

    public static bool TryGetMethod(string generatedMethodName, out DetouredFuncInfo detouredFuncInfo)
    {
        return _trampolines.TryGetValue(generatedMethodName, out detouredFuncInfo);
    }

    public static void Remove(string generatedMethodName)
    {
        if (!TryGetMethod(generatedMethodName, out var detouredFuncInfo))
            return;

        if (detouredFuncInfo.PreHook != null || detouredFuncInfo.PostHook != null)
            throw new Exception(
                $"DetouredFuncInfo to remove still had one or more hooks. Func Name: {detouredFuncInfo.Name} Class: {detouredFuncInfo.DeclaringClass}");

        _trampolines.Remove(generatedMethodName);
    }

    public static DetouredFuncInfo GetOrCreateMethod(TypeInfo targetType, UndecoratedFunction targetMethod, Type retType, string generatedMethodName)
    {
        DetouredFuncInfo detouredFuncInfo;

        string key = $"{targetType.ModuleName}!{generatedMethodName}";
        if (!TryGetMethod(key, out detouredFuncInfo))
        {
            (var generatedMethodInfo, var generatedDelegate, var delType) = GenerateMethodForName(targetMethod.NumArgs.Value, retType, key);

            detouredFuncInfo = new DetouredFuncInfo(targetType, targetMethod, generatedMethodName, generatedMethodInfo,
                generatedDelegate, delType);

            _trampolines[key] = detouredFuncInfo;
        }

        return detouredFuncInfo;
    }



    private static (MethodInfo, Delegate, Type) GenerateMethodForName(int numArguments, Type retType, string generatedMethodName)
    {
        // Create a dynamic method with the specified return type and parameter types
        DynamicMethod dynamicMethod = new DynamicMethod(generatedMethodName, retType, GetParameterTypes(numArguments));

        // Get the IL generator for the dynamic method
        ILGenerator il = dynamicMethod.GetILGenerator();

        // Load the generatedMethodName as a constant string
        //il.Emit(OpCodes.Ldstr, generatedMethodName);

        // Create an array to hold the arguments
        LocalBuilder argsLocal = il.DeclareLocal(typeof(object[]));
        il.Emit(OpCodes.Ldc_I4, numArguments); // Load the number of arguments
        il.Emit(OpCodes.Newarr, typeof(object)); // Create a new object array with the specified length
        il.Emit(OpCodes.Stloc, argsLocal); // Store it in the local variable

        // Load the arguments into the array
        for (int i = 0; i < numArguments; i++)
        {
            il.Emit(OpCodes.Ldloc, argsLocal); // Load the array
            il.Emit(OpCodes.Ldc_I4, i); // Load the index
            il.Emit(OpCodes.Ldarg, i); // Load the corresponding argument
            il.Emit(OpCodes.Box, typeof(nuint)); // Box the nuint argument
            il.Emit(OpCodes.Stelem_Ref); // Store the argument in the array
        }

        // Call the Unified method
        il.Emit(OpCodes.Ldstr, generatedMethodName);
        il.Emit(OpCodes.Ldloc, argsLocal); // Load the array
        il.Emit(OpCodes.Call, typeof(DetoursMethodGenerator).GetMethod(nameof(Unified))); // Call the Unified method
        // Return the result from the dynamic method
        il.Emit(OpCodes.Ret);

        // Create a delegate from the dynamic method
        Type delType = NativeDelegatesFactory.GetDelegateType(retType, numArguments);
        Delegate del = dynamicMethod.CreateDelegate(delType);

        // Return the MethodInfo and the delegate
        return (dynamicMethod, del, delType);

        static Type[] GetParameterTypes(int numArguments)
        {
            Type[] parameterTypes = new Type[numArguments];

            for (int i = 0; i < numArguments; i++)
            {
                parameterTypes[i] = typeof(nuint); // Parameters are of type long
            }

            return parameterTypes;
        }
    }

    /// <summary>
    /// Unified patch entry point. Called by the Dynamic Method returned from <see cref="GenerateMethodForName"/>.
    /// </summary>
    /// <param name="generatedMethodName">The name of the generated Dynamic Method.</param>
    /// <param name="args">Native arguments provided to the hooked function</param>
    /// <returns>Result of the original function invocation, or return value if overriden by the prefix patch.</returns>
    public static nuint Unified(string generatedMethodName, params object[] args)
    {
        DetouredFuncInfo tramp = _trampolines[generatedMethodName];

        // Call prefix hook
        bool skipOriginal = RunPatchInPosition(HarmonyPatchPosition.Prefix, tramp, args, out nuint overridenReturnValue);
        if (!skipOriginal)
        {
            overridenReturnValue = 0;

            // Call original method
            Delegate realMethod = DelegateStore.Real[tramp.GenerateMethodInfo];
            object res = realMethod.DynamicInvoke(args);
            if (res != null)
                overridenReturnValue = (nuint)res;
        }

        // Call postfix hook
        // TODO: Pass real method's result to the hook
        RunPatchInPosition(HarmonyPatchPosition.Postfix, tramp, args, out nuint _);

        return overridenReturnValue;
    }


    /// <returns>Boolean indicating 'skipOriginal'</returns>
    static bool RunPatchInPosition(HarmonyPatchPosition position, DetouredFuncInfo hookedFunc, object[] args, out nuint overriddenReturnValue)
    {
        overriddenReturnValue = 0;
        if (args.Length == 0) throw new Exception("Bad arguments to unmanaged HookCallback. Expecting at least 1 (for 'this').");

        object self = new NativeObject((nuint)args.FirstOrDefault(), hookedFunc.DeclaringClass);

        // Args without self
        object[] argsToForward = new object[args.Length - 1];
        for (int i = 0; i < argsToForward.Length; i++)
        {
            nuint arg = (nuint)args[i + 1];

            string argType = hookedFunc.Target.ArgTypes[i + 1];
            if (argType == "char*" || argType == "char *")
            {
                if (arg != 0)
                {
                    string cString = Marshal.PtrToStringAnsi(new IntPtr((long)arg));
                    argsToForward[i] = new CharStar(arg, cString);
                }
                else
                {
                    argsToForward[i] = arg;
                }
            }
            else if (argType.EndsWith('*'))
            {
                // If the argument is a pointer, indicate it with a NativeObject
                // TODO: SecondClassTypeInfo is abused here
                string fixedArgType = argType[..^1].Trim();
                argsToForward[i] = new NativeObject(arg, new SecondClassTypeInfo(hookedFunc.DeclaringClass.ModuleName, fixedArgType));
            }
            else
            {
                // Primitive or struct or something else crazy
                argsToForward[i] = arg;
            }
        }


        bool skipOriginal = false;
        object newRetVal = null;
        bool retValModified;

        if (position == HarmonyPatchPosition.Prefix && hookedFunc.PreHook != null)
        {
            skipOriginal = hookedFunc.PreHook(self, argsToForward, ref newRetVal);
            retValModified = skipOriginal;
        }
        else if (position == HarmonyPatchPosition.Postfix && hookedFunc.PostHook != null)
        {
            // Post hook can't ask to "Skip Original", it was already invoke.
            // It can only signal to use if the return value changes.
            retValModified = hookedFunc.PostHook(self, argsToForward, ref newRetVal);
        }
        else
        {
            throw new Exception($"Unexpected hook position fired. Pos: {position} , Method: {hookedFunc.Name}");
        }

        if (retValModified)
        {
            if (newRetVal is not nuint newNuintRetVal)
            {
                throw new ArgumentException($"Return value from {position} hook was NOT an nuint. It was: {newRetVal}");
            }
            overriddenReturnValue = newNuintRetVal;
        }

        return skipOriginal;
    }

}