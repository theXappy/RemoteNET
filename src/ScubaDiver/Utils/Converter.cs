using System;
using System.Reflection.Emit;

namespace ScubaDiver
{
    /// <summary>
    /// This mind bending class is taken from https://github.com/denandz/KeeFarce
    /// denandz credited Alois Kraus for this part then I'm doing the same.
    /// </summary>
    public class Converter<T>
    {
        delegate U Void2ObjectConverter<U>(IntPtr pManagedObject);
        static Void2ObjectConverter<T> myConverter;

        // The type initializer is run every time the converter is instantiated with a different 
        // generic argument. 
        static Converter()
        {
            GenerateDynamicMethod();
        }

        static void GenerateDynamicMethod()
        {
            if (myConverter == null)
            {
                Console.WriteLine("[Diver] Dynamic Method init");
                DynamicMethod method = new DynamicMethod("ConvertPtrToObjReference", typeof(T), new Type[] { typeof(IntPtr) }, typeof(IntPtr), true);
                var gen = method.GetILGenerator();
                // Load first argument 
                gen.Emit(OpCodes.Ldarg_0);
                // return it directly. The Clr will take care of the cast!
                // this construct is unverifiable so we need to plug this into an assembly with 
                // IL Verification disabled
                gen.Emit(OpCodes.Ret);
                myConverter = (Void2ObjectConverter<T>)method.CreateDelegate(typeof(Void2ObjectConverter<T>));
                Console.WriteLine("[Diver] init done");
            }
        }

        public T ConvertFromIntPtr(IntPtr pObj)
        {
            return myConverter(pObj);
        }

        public T ConvertFromIntPtr(ulong pObj) => ConvertFromIntPtr(new IntPtr((long) pObj));
    }
}