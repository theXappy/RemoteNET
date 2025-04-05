using System;
using System.Collections.Generic;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using System.Threading;

namespace ScubaDiver
{
    public static class FreezeFuncsFactory
    {
        public delegate void FreezeFunc(object[] objects, ulong[] addresses, ManualResetEvent frozenFeedback,
            ManualResetEvent unfreezeRequested);

        private static Dictionary<int, FreezeFunc> _dict = new();

        public static FreezeFunc Generate(int numArguments)
        {
            if (!_dict.TryGetValue(numArguments, out FreezeFunc func))
            {
                func = GenerateInternal(numArguments);
                _dict[numArguments] = func;
            }
            return func;
        }


        private static FreezeFunc GenerateInternal(int numArguments)
        {
            // Create a dynamic method with the desired number of parameters
            var freezeMethod = new DynamicMethod(
                "FreezeInternal_"+numArguments,
                typeof(void),
                new[]
                {
                    typeof(object[]), // objs
                    typeof(ulong[]), // addr_param
                    typeof(ManualResetEvent), // frozenFeedback
                    typeof(ManualResetEvent) // unfreezeRequested
                },
                typeof(FreezeFuncsFactory)
            );

            // Generate the method body
            ILGenerator il = freezeMethod.GetILGenerator();

            // Create IL locals:
            // [0]      byte*
            // [1]      byte*
            // ...
            // [N - 1]  byte*
            //
            // [N]      byte& pinned
            // [N + 1]  byte& pinned
            // ...
            // [2N - 1] byte& pinned
            //
            // [2N]     IntPtr
            var locals = new LocalBuilder[2 * numArguments + 1];
            for (int i = 0; i < numArguments; i++)
            {
                locals[i] = il.DeclareLocal(typeof(byte*));
            }
            for (int i = 0; i < numArguments; i++)
            {
                locals[numArguments + i] = il.DeclareLocal(typeof(byte).MakeByRefType(), pinned: true);
            }
            locals[2 * numArguments] = il.DeclareLocal(typeof(IntPtr));

            il.Emit(OpCodes.Nop);

            // Pin every single object and set it's value in the addresses array
            for (int i = 0; i < numArguments; i++)
            {
                // These lines load the next object:
                // `a = objs[i]`
                il.Emit(OpCodes.Ldarg_0);
                il.Emit(OpCodes.Ldc_I4, i);
                il.Emit(OpCodes.Ldelem_Ref);

                // Case to Pinnable to trick runtime
                // `b = Unsafe.As<Pinnable>(a)`
                il.Emit(OpCodes.Call,
                    typeof(Unsafe).GetMethod("As", new Type[1] { typeof(object) })
                        .MakeGenericMethod(typeof(Pinnable)));

                // Load addess of fake "Data" field
                // `c = &b.Data`
                il.Emit(OpCodes.Ldflda, typeof(Pinnable).GetField("Data"));

                // Pin the object by inserting into a pinned local
                // `pinned_local_i = c`
                il.Emit(OpCodes.Stloc, numArguments + i);
                // Save the address
                // addr_local_i = (long)pinned_local_i
                il.Emit(OpCodes.Ldloc, numArguments + i);
                il.Emit(OpCodes.Conv_U);
                il.Emit(OpCodes.Stloc, i);

                // Generate temporary IntPtr
                // `int_ptr_local = new IntPtr((void*)addr_local_i)`
                il.Emit(OpCodes.Ldloca, locals[2 * numArguments]);
                il.Emit(OpCodes.Ldloc, i);
                il.Emit(OpCodes.Call, typeof(IntPtr).GetConstructor(new[] { typeof(void*) }));


                // Our fixed pointer to the first field of the class lets
                // us calculate the address to the object.
                // We have:
                //                 🠗
                // [ Method Table ][ Field 1 ][ Field 2 ]...
                //
                // And we want: 
                // 🠗
                // [ Method Table ][ Field 1 ][ Field 2 ]...
                //
                // As far as I understand the Method Table is a pointer which means
                // it's 4 bytes in x32 and 8 bytes in x64 (Hence using `IntPtr.Size`)
                //
                // Store the address in the addreses array parameter
                // `addr_param[i] = int_ptr_local.ToInt64 - IntPtr.Size;
                il.Emit(OpCodes.Ldarg_1);
                il.Emit(OpCodes.Ldc_I4, i);
                il.Emit(OpCodes.Ldloca, locals[2 * numArguments]);
                il.Emit(OpCodes.Call, typeof(IntPtr).GetMethod("ToInt64"));
                il.Emit(OpCodes.Call, typeof(IntPtr).GetMethod("get_Size"));
                il.Emit(OpCodes.Conv_I8);
                il.Emit(OpCodes.Sub);
                il.Emit(OpCodes.Stelem_I8);
            }


            // Signal that the objects have been pinned
            // `frozenFeedback.Set()`
            il.Emit(OpCodes.Ldarg_2);
            il.Emit(OpCodes.Callvirt, typeof(ManualResetEvent).GetMethod("Set"));
            il.Emit(OpCodes.Pop);

            // Wait for the unfreeze request
            // `unfreezeRequested.WaitOne()`
            il.Emit(OpCodes.Ldarg_3);
            il.Emit(OpCodes.Callvirt, typeof(WaitHandle).GetMethod("WaitOne", Array.Empty<Type>()));
            il.Emit(OpCodes.Pop);


            // Return
            il.Emit(OpCodes.Ret);

            // Return a delegate for the method
            return (FreezeFunc)freezeMethod.CreateDelegate(typeof(FreezeFunc));
        }
    }
}
