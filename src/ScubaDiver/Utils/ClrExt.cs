using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using Microsoft.Diagnostics.Runtime;
using ScubaDiver.Utils;

namespace ScubaDiver.Utils
{
    public static class ClrExt
    {
        public static byte[] ToByteArray(this ClrArray arr)
        {
            try
            {
                arr.GetValue<byte>(0);
            }
            catch (Exception ex)
            {
                // ???
                throw new ArgumentException("Not a byte array", ex);
            }

            byte[] res = new byte[arr.Length];
            for (int i = 0; i < res.Length; i++)
            {
                res[i] = arr.GetValue<byte>(i);
            }
            return res;
        }

        public static byte[] ToByteArray(this ClrObject obj)
        {
            return obj.AsArray().ToByteArray();
        }


        public class TypeDefToMethod
        {
            public ulong MethodTable { get; set; }
            public int Token { get; set; }
        }
        public static IEnumerable<TypeDefToMethod> OldSchoolEnumerateTypeDefToMethodTableMap(this ClrModule mod)
        {
            //EnumerateTypeDefToMethodTableMap wants to return an IEnumerable<(ulong,int)> to us but returning tuples costs
            //us another dependency so we're avoiding it.
            IEnumerable misteriousEnumerable = typeof(ClrModule).GetMethod("EnumerateTypeDefToMethodTableMap").Invoke(mod, new object[0]) as IEnumerable;
            foreach (object o in misteriousEnumerable)
            {
                var type = o.GetType();
                ulong mt = (ulong)type.GetField("Item1").GetValue(o);
                int token = (int)type.GetField("Item2").GetValue(o);
                yield return new TypeDefToMethod() { MethodTable = mt, Token = token };
            }
        }
    }
}