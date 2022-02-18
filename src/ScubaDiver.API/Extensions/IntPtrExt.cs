using System;
using System.Runtime.InteropServices;

namespace ScubaDiver.API.Extensions
{
    public static class IntPtrExt
    {
        public static IntPtr GetMethodTable(this IntPtr o)
        {
            try
            {
                IntPtr methodTable = Marshal.ReadIntPtr(o);
                return methodTable;
            }
            catch (Exception e)
            {
                throw new AccessViolationException("Failed to read MethodTable at the object's address.", e);
            }
        }
    }

}
