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

    public static DetouredFuncInfo GetOrCreateMethod(TypeInfo targetType, UndecoratedFunction targetMethod, Type retType, string generatedMethodName)
    {
        DetouredFuncInfo detouredFuncInfo;

        if (!TryGetMethod(generatedMethodName, out detouredFuncInfo))
        {
            (var generatedMethodInfo, var generatedDelegate, var delType) = GenerateMethodForName(targetMethod.NumArgs.Value, retType, generatedMethodName);

            detouredFuncInfo = new DetouredFuncInfo(targetType, targetMethod, generatedMethodName, generatedMethodInfo,
                generatedDelegate, delType);

            _trampolines[generatedMethodName] = detouredFuncInfo;
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
        il.Emit(OpCodes.Call, typeof(DetoursMethodGenerator).GetMethod("Unified")); // Call the Unified method
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


    public static nuint Unified(string generatedMethodName, params object[] args)
    {
        DetouredFuncInfo tramp = _trampolines[generatedMethodName];

        // Call prefix hook
        bool skipOriginal = Hook(HarmonyPatchPosition.Prefix, tramp, args, out nuint overridenReturnValue);
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
        Hook(HarmonyPatchPosition.Postfix, tramp, args, out nuint _);

        return overridenReturnValue;
    }


    static bool Hook(HarmonyPatchPosition pos, DetouredFuncInfo tramp, object[] args, out nuint value)
    {
        if (args.Length == 0) throw new Exception("Bad arguments to unmanaged HookCallback. Expecting at least 1 (for 'this').");

        object self = new NativeObject((nuint)args.FirstOrDefault(), tramp.DeclaringClass);

        // Args without self
        object[] argsToForward = new object[args.Length - 1];
        for (int i = 0; i < argsToForward.Length; i++)
        {
            nuint arg = (nuint)args[i + 1];

            // If the argument is a pointer, indicate it with a NativeObject
            string argType = tramp.Target.ArgTypes[i + 1];
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
                // TODO: SecondClassTypeInfo is abused here
                string fixedArgType = argType[..^1].Trim();
                argsToForward[i] = new NativeObject(arg, new SecondClassTypeInfo(tramp.DeclaringClass.ModuleName, fixedArgType));
            }
            else
            {
                // Primitive or struct or something else crazy
                argsToForward[i] = arg;
            }
        }


        // TODO: We're currently ignoring the "skip original" return value because the callback
        // doesn't support setting the return value...
        if (pos == HarmonyPatchPosition.Prefix)
        {
            if (tramp.PreHook == null)
            {
                // Don't skip original if no hook
                value = 0;
                return false;
            }
            tramp.PreHook(self, argsToForward);
        }
        else if (pos == HarmonyPatchPosition.Postfix)
        {
            if (tramp.PostHook == null)
            {
                // Return value is meaningless in post hooks anyway
                value = 0;
                return false;
            }
            tramp.PostHook(self, argsToForward);
        }
        else
        {
            throw new Exception($"Unexpected hook position fired. Pos: {pos} , Method: {tramp.Name}");
        }

        value = 0;
        bool skipOriginal = false;
        return skipOriginal;
    }

}