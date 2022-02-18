using System;
using System.Reflection.Emit;
using ScubaDiver.API.Extensions;

namespace ScubaDiver.Utils
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
                DynamicMethod method = new("ConvertPtrToObjReference", typeof(T), new Type[] { typeof(IntPtr) }, typeof(IntPtr), true);
                var gen = method.GetILGenerator();
                // Load first argument 
                gen.Emit(OpCodes.Ldarg_0);
                // return it directly. The Clr will take care of the cast!
                // this construct is unverifiable so we need to plug this into an assembly with 
                // IL Verification disabled
                gen.Emit(OpCodes.Ret);
                myConverter = (Void2ObjectConverter<T>)method.CreateDelegate(typeof(Void2ObjectConverter<T>));
            }
        }

        public T ConvertFromIntPtr(IntPtr pObj, IntPtr expectedMethodTable)
        {
            // Reading Method Table (MT) of the object to make sure we 
            // aren't mistakenly pointing at another type by now (could be caused by the GC)
            IntPtr actualMethodTable = pObj.GetMethodTable();
            if (actualMethodTable != expectedMethodTable)
            {
                throw new ArgumentException("Actual Method Table value was not as expected");
            }
            return myConverter(pObj);
        }

        public T ConvertFromIntPtr(ulong pObj, ulong expectedMethodTable) =>
            ConvertFromIntPtr(
                new IntPtr((long) pObj),
                new IntPtr((long) expectedMethodTable));
    }
}