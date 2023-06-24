using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.InteropServices;
using DetoursNet;

namespace ScubaDiver;

public static class DetoursMethodGenerator
{
    public class DetoursTrampoline : IDisposable
    {
        public MethodInfo GenerateMethodInfo { get; set; }
        public Delegate GeneratedDelegate { get; set; }
        public Type DelegateType { get; set; }
        public string Name { get; set; }
        private DetoursWrapperCallback _callback;
        public DetoursTrampoline(string name, MethodInfo generateMethodInfo, Delegate generatedDelegate, Type delegateType, DetoursWrapperCallback callback)
        {
            GenerateMethodInfo = generateMethodInfo;
            GeneratedDelegate = generatedDelegate;
            DelegateType = delegateType;
            Name = name;
            _callback = callback;
            DetoursMethodGenerator._callbacks[Name] = _callback;
            DetoursMethodGenerator._trampolines[Name] = this;
        }

        public void Dispose()
        {
            DetoursMethodGenerator._callbacks.Remove(Name);
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

    public delegate nuint DetoursWrapperCallback(DetoursTrampoline tramp, object[] args);

    public static DetoursTrampoline GenerateMethod(int numArguments, Type retType, string generatedMethodName, DetoursWrapperCallback callback)
    {
        MethodInfo unifiedMethod = typeof(DetoursMethodGenerator).GetMethod("Unified");

        (var generatedMethodInfo, var generatedDelegate, var delType) = GenerateMethodForName(unifiedMethod, numArguments, retType, generatedMethodName);

        return new DetoursTrampoline(generatedMethodName, generatedMethodInfo, generatedDelegate, delType, callback);
    }


    private static (MethodInfo, Delegate, Type) GenerateMethodForName(MethodInfo unifiedMethod, int numArguments, Type retType, string generatedMethodName)
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
        il.Emit(OpCodes.Call, unifiedMethod); // Call the Unified method
        // Return the result from the dynamic method
        il.Emit(OpCodes.Ret);

        // Create a delegate from the dynamic method
        Type delType = GetDelegateType(retType, numArguments);
        Delegate del = dynamicMethod.CreateDelegate(delType);

        // Return the MethodInfo and the delegate
        return (dynamicMethod, del, delType);
    }

    private static Dictionary<string, DetoursWrapperCallback> _callbacks = new();
    private static Dictionary<string, DetoursTrampoline> _trampolines = new();

    public static nuint Unified(string generatedMethodName , params object[] args)
    {
        DetoursTrampoline tramp = _trampolines[generatedMethodName];
        return _callbacks[generatedMethodName](tramp, args);
    }

    private static Type[] GetParameterTypes(int numArguments)
    {
        Type[] parameterTypes = new Type[numArguments];

        for (int i = 0; i < numArguments; i++)
        {
            parameterTypes[i] = typeof(nuint); // Parameters are of type long
        }

        return parameterTypes;
    }


    delegate void FuncVoidArgs0();
    delegate void FuncVoidArgs1(nuint _1);
    delegate void FuncVoidArgs2(nuint _1, nuint _2);
    delegate void FuncVoidArgs3(nuint _1, nuint _2, nuint _3);
    delegate void FuncVoidArgs4(nuint _1, nuint _2, nuint _3, nuint _4);
    delegate void FuncVoidArgs5(nuint _1, nuint _2, nuint _3, nuint _4, nuint _5);
    delegate void FuncVoidArgs6(nuint _1, nuint _2, nuint _3, nuint _4, nuint _5, nuint _6);
    delegate void FuncVoidArgs7(nuint _1, nuint _2, nuint _3, nuint _4, nuint _5, nuint _6, nuint _7);
    delegate void FuncVoidArgs8(nuint _1, nuint _2, nuint _3, nuint _4, nuint _5, nuint _6, nuint _7, nuint _8);
    delegate void FuncVoidArgs9(nuint _1, nuint _2, nuint _3, nuint _4, nuint _5, nuint _6, nuint _7, nuint _8, nuint _9);
    delegate void FuncVoidArgs10(nuint _1, nuint _2, nuint _3, nuint _4, nuint _5, nuint _6, nuint _7, nuint _8, nuint _9, nuint _10);
    delegate nuint FuncNUIntArgs0();
    delegate nuint FuncNUIntArgs1(nuint _1);
    delegate nuint FuncNUIntArgs2(nuint _1, nuint _2);
    delegate nuint FuncNUIntArgs3(nuint _1, nuint _2, nuint _3);
    delegate nuint FuncNUIntArgs4(nuint _1, nuint _2, nuint _3, nuint _4);
    delegate nuint FuncNUIntArgs5(nuint _1, nuint _2, nuint _3, nuint _4, nuint _5);
    delegate nuint FuncNUIntArgs6(nuint _1, nuint _2, nuint _3, nuint _4, nuint _5, nuint _6);
    delegate nuint FuncNUIntArgs7(nuint _1, nuint _2, nuint _3, nuint _4, nuint _5, nuint _6, nuint _7);
    delegate nuint FuncNUIntArgs8(nuint _1, nuint _2, nuint _3, nuint _4, nuint _5, nuint _6, nuint _7, nuint _8);
    delegate nuint FuncNUIntArgs9(nuint _1, nuint _2, nuint _3, nuint _4, nuint _5, nuint _6, nuint _7, nuint _8, nuint _9);
    delegate nuint FuncNUIntArgs10(nuint _1, nuint _2, nuint _3, nuint _4, nuint _5, nuint _6, nuint _7, nuint _8, nuint _9, nuint _10);

    private static Type GetDelegateType(Type returnType, int numArguments)
    {
        string delegateTypeName = $"Func{TypeToTypeName(returnType)}Args{numArguments}";
        return typeof(DetoursMethodGenerator).GetNestedType(delegateTypeName, (BindingFlags)0xffff);
    }

    private static string TypeToTypeName(Type type)
    {
        if (type == typeof(void))
            return "Void";
        else if (type == typeof(nuint))
            return "NUInt";
        else
            throw new ArgumentException("Unsupported return type.");
    }
}