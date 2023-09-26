using System;
using System.Reflection;

namespace ScubaDiver;

public static class NativeDelegatesFactory
{
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
    delegate void FuncVoidArgs11(nuint _1, nuint _2, nuint _3, nuint _4, nuint _5, nuint _6, nuint _7, nuint _8, nuint _9, nuint _10, nuint _11);
    delegate void FuncVoidArgs12(nuint _1, nuint _2, nuint _3, nuint _4, nuint _5, nuint _6, nuint _7, nuint _8, nuint _9, nuint _10, nuint _11, nuint _12);
    delegate void FuncVoidArgs13(nuint _1, nuint _2, nuint _3, nuint _4, nuint _5, nuint _6, nuint _7, nuint _8, nuint _9, nuint _10, nuint _11, nuint _12, nuint _13);
    delegate void FuncVoidArgs14(nuint _1, nuint _2, nuint _3, nuint _4, nuint _5, nuint _6, nuint _7, nuint _8, nuint _9, nuint _10, nuint _11, nuint _12, nuint _13, nuint _14);
    delegate void FuncVoidArgs15(nuint _1, nuint _2, nuint _3, nuint _4, nuint _5, nuint _6, nuint _7, nuint _8, nuint _9, nuint _10, nuint _11, nuint _12, nuint _13, nuint _14, nuint _15);
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
    delegate nuint FuncNUIntArgs11(nuint _1, nuint _2, nuint _3, nuint _4, nuint _5, nuint _6, nuint _7, nuint _8, nuint _9, nuint _10, nuint _11);
    delegate nuint FuncNUIntArgs12(nuint _1, nuint _2, nuint _3, nuint _4, nuint _5, nuint _6, nuint _7, nuint _8, nuint _9, nuint _10, nuint _11, nuint _12);
    delegate nuint FuncNUIntArgs13(nuint _1, nuint _2, nuint _3, nuint _4, nuint _5, nuint _6, nuint _7, nuint _8, nuint _9, nuint _10, nuint _11, nuint _12, nuint _13);
    delegate nuint FuncNUIntArgs14(nuint _1, nuint _2, nuint _3, nuint _4, nuint _5, nuint _6, nuint _7, nuint _8, nuint _9, nuint _10, nuint _11, nuint _12, nuint _13, nuint _14);
    delegate nuint FuncNUIntArgs15(nuint _1, nuint _2, nuint _3, nuint _4, nuint _5, nuint _6, nuint _7, nuint _8, nuint _9, nuint _10, nuint _11, nuint _12, nuint _13, nuint _14, nuint _15);

    public static Type GetDelegateType(Type returnType, int numArguments)
    {
        string delegateTypeName = $"Func{TypeToTypeName(returnType)}Args{numArguments}";
        return typeof(FuncVoidArgs0).DeclaringType!.GetNestedType(delegateTypeName, (BindingFlags)0xffff);
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