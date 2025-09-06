using System;
using System.Diagnostics;
using System.Drawing;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace ScubaDiver.API.Memory
{
    /// <summary>
    /// Like System.Runtime.InteropServices.Marshal, but throws catch-able exceptions
    /// instead of AccessViolations.
    /// </summary>
    public static class SafeMarshal
    {
        private static IntPtr SingleValidByte = Marshal.AllocHGlobal(1);

        public static IntPtr AllocHGlobal(int cb)
        {
            return Marshal.AllocHGlobal(cb);
        }

        public static IntPtr AllocHGlobalZero(int cb)
        {
            IntPtr ptr = Marshal.AllocHGlobal(cb);
            for (int i = 0; i < cb; i++)
                Marshal.WriteByte(ptr, 0x00);
            return ptr;
        }

        public static void FreeHGlobal(IntPtr hglobal)
        {
            Marshal.FreeHGlobal(hglobal);
        }

        public static void Copy(byte[] source, int startIndex, IntPtr destination, int length)
        {
            CheckMemoryAccess(destination, length, false);
            Marshal.Copy(source, startIndex, destination, length);
        }

        public static void Copy(IntPtr source, byte[] destination, int startIndex, int length)
        {
            CheckMemoryAccess(source, length, true);
            Marshal.Copy(source, destination, startIndex, length);
        }

        public static string PtrToStringAnsi(IntPtr ptr)
        {
            CheckMemoryAccess(ptr, 0, true);
            return Marshal.PtrToStringAnsi(ptr);
        }

        /// <summary>
        /// Sets a byte[] to zero.
        /// </summary>
        public static void MemSetZero(byte[] arr)
        {
            for (int i = 0; i < arr.Length; i++)
                arr[i] = 0x00;
        }

        private static void CheckMemoryAccess(IntPtr ptr, int length, bool read)
        {
            IntPtr processHandle = Process.GetCurrentProcess().Handle;
            if (processHandle == IntPtr.Zero)
            {
                // Handle error opening process
                throw new InvalidOperationException("Failed to open process for memory access check.");
            }

            bool success = NativeMethods.ReadProcessMemory(processHandle, ptr, SingleValidByte, 1, out _);
            if (!success)
            {
                throw new InvalidOperationException("Memory access check failed.");
            }

            if (!read)
            {
                // Checking 'Write' involves both READ (as we did above) and WRITE (next line)
                success = NativeMethods.WriteProcessMemory(processHandle, ptr, SingleValidByte, 1, out _);
            }

            if (!success)
            {
                throw new InvalidOperationException("Memory access check failed.");
            }
        }

        internal static class NativeMethods
        {
            [DllImport("kernel32.dll", SetLastError = true)]
            [return: MarshalAs(UnmanagedType.Bool)]
            public static extern bool ReadProcessMemory(IntPtr hProcess, IntPtr lpBaseAddress, IntPtr lpBuffer, uint nSize, out int lpNumberOfBytesRead);

            [DllImport("kernel32.dll", SetLastError = true)]
            [return: MarshalAs(UnmanagedType.Bool)]
            public static extern bool WriteProcessMemory(IntPtr hProcess, IntPtr lpBaseAddress, IntPtr lpBuffer, uint nSize, out int lpNumberOfBytesWritten);
        }
    }
}
