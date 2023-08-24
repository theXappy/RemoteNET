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
        public UndecoratedFunction Target { get; set; }
        public MethodInfo GenerateMethodInfo { get; set; }
        public Delegate GeneratedDelegate { get; set; }
        public Type DelegateType { get; set; }
        public string Name { get; set; }
        private DetoursWrapperCallback _callback;
        public DetoursTrampoline(UndecoratedFunction target, string name, MethodInfo generateMethodInfo, Delegate generatedDelegate, Type delegateType, DetoursWrapperCallback callback)
        {
            Target = target;
            GenerateMethodInfo = generateMethodInfo;
            GeneratedDelegate = generatedDelegate;
            DelegateType = delegateType;
            Name = name;
            _callback = callback;
            _callbacks[Name] = _callback;
            _trampolines[Name] = this;
        }

        public void Dispose()
        {
            _callbacks.Remove(Name);
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

    public static DetoursTrampoline GenerateMethod(UndecoratedFunction target, Type retType, string generatedMethodName, DetoursWrapperCallback callback)
    {
        MethodInfo unifiedMethod = typeof(DetoursMethodGenerator).GetMethod("Unified");

        (var generatedMethodInfo, var generatedDelegate, var delType) = GenerateMethodForName(unifiedMethod, target.NumArgs.Value, retType, generatedMethodName);

        return new DetoursTrampoline(target, generatedMethodName, generatedMethodInfo, generatedDelegate, delType, callback);
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
        Type delType = NativeDelegatesFactory.GetDelegateType(retType, numArguments);
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

}