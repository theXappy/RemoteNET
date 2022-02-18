using Microsoft.Win32.SafeHandles;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace RemoteNET.Internal.Extensions
{
    public static class ProcessExt
    {
        [DebuggerDisplay("{" + nameof(szModule) + "}")]
        [StructLayout(LayoutKind.Sequential)]
        public struct MODULEENTRY32
        {
            public uint dwSize;
            public uint th32ModuleID;
            public uint th32ProcessID;
            public uint GlblcntUsage;
            public uint ProccntUsage;
            public readonly IntPtr modBaseAddr;
            public uint modBaseSize;
            public readonly IntPtr hModule;

            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)]
            public string szModule;

            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)]
            public string szExePath;
        }

        /// <summary>
        /// A utility class to determine a process parent.
        /// </summary>
        [StructLayout(LayoutKind.Sequential)]
        public struct ParentProcessUtilities
        {
            // These members must match PROCESS_BASIC_INFORMATION
            internal IntPtr Reserved1;
            internal IntPtr PebBaseAddress;
            internal IntPtr Reserved2_0;
            internal IntPtr Reserved2_1;
            internal IntPtr UniqueProcessId;
            internal IntPtr InheritedFromUniqueProcessId;

        }
        public class ToolHelpHandle : SafeHandleZeroOrMinusOneIsInvalid
        {
            private ToolHelpHandle() : base(true) { }

            protected override bool ReleaseHandle()
            {
                return CloseHandle(this.handle);
            }
        }
        [Flags]
        public enum SnapshotFlags : uint
        {
            HeapList = 0x00000001,
            Process = 0x00000002,
            Thread = 0x00000004,
            Module = 0x00000008,
            Module32 = 0x00000010,
            Inherit = 0x80000000,
            All = 0x0000001F
        }
        [DllImport("ntdll.dll")]
        private static extern int NtQueryInformationProcess(IntPtr processHandle, int processInformationClass, ref ParentProcessUtilities processInformation, int processInformationLength, out int returnLength);

        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern ToolHelpHandle CreateToolhelp32Snapshot(SnapshotFlags dwFlags, int th32ProcessID);

        [DllImport("kernel32.dll")]
        public static extern bool Module32First(ToolHelpHandle hSnapshot, ref MODULEENTRY32 lpme);

        [DllImport("kernel32.dll")]
        public static extern bool Module32Next(ToolHelpHandle hSnapshot, ref MODULEENTRY32 lpme);

        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern bool CloseHandle(IntPtr hHandle);

        /// <summary>
        /// Gets the parent process of the current process.
        /// </summary>
        /// <returns>An instance of the Process class.</returns>
        public static Process GetParentProcess()
        {
            return GetParentProcess(Process.GetCurrentProcess().Handle);
        }

        /// <summary>
        /// Gets the parent process of specified process.
        /// </summary>
        /// <param name="id">The process id.</param>
        /// <returns>An instance of the Process class.</returns>
        public static Process GetParentProcess(int id)
        {
            Process process = Process.GetProcessById(id);
            return GetParentProcess(process.Handle);
        }

        /// <summary>
        /// Gets the parent process of a specified process.
        /// </summary>
        /// <param name="handle">The process handle.</param>
        /// <returns>An instance of the Process class.</returns>
        public static Process GetParentProcess(IntPtr handle)
        {
            ParentProcessUtilities pbi = new ParentProcessUtilities();
            int returnLength;
            int status = NtQueryInformationProcess(handle, 0, ref pbi, Marshal.SizeOf(pbi), out returnLength);
            if (status != 0)
                throw new Win32Exception(status);

            try
            {
                return Process.GetProcessById(pbi.InheritedFromUniqueProcessId.ToInt32());
            }
            catch (ArgumentException)
            {
                // not found
                return null;
            }
        }

        public static Process GetParent(this Process proc) => GetParentProcess(proc.Id);

        public static IEnumerable<MODULEENTRY32> GetModules(int processId)
        {
            var me32 = default(MODULEENTRY32);
            var hModuleSnap = CreateToolhelp32Snapshot(SnapshotFlags.Module | SnapshotFlags.Module32, processId);

            if (hModuleSnap.IsInvalid)
            {
                yield break;
            }

            using (hModuleSnap)
            {
                me32.dwSize = (uint)Marshal.SizeOf(me32);

                if (Module32First(hModuleSnap, ref me32))
                {
                    do
                    {
                        yield return me32;
                    }
                    while (Module32Next(hModuleSnap, ref me32));
                }
            }
        }
        public static IEnumerable<MODULEENTRY32> GetModules(Process process) => GetModules(process.Id);


        public static string GetSupportedTargetFramework(this Process process)
        {
            var modules = GetModules(process);

            // ReSharper disable once IdentifierTypo
            // ReSharper disable once InconsistentNaming
            var wpfGfxForCoreFrameworkFound = false;
            FileVersionInfo hostPolicyVersionInfo = null;

            foreach (var module in modules)
            {
                if (module.szModule.StartsWith("hostpolicy.dll", StringComparison.OrdinalIgnoreCase))
                {
                    hostPolicyVersionInfo = FileVersionInfo.GetVersionInfo(module.szExePath);
                }

                if (module.szModule.StartsWith("wpfgfx_cor3.dll", StringComparison.OrdinalIgnoreCase)
                    || module.szModule.StartsWith("wpfgfx_net6.dll", StringComparison.OrdinalIgnoreCase))
                {
                    wpfGfxForCoreFrameworkFound = true;
                }

                if (wpfGfxForCoreFrameworkFound
                    && hostPolicyVersionInfo != null)
                {
                    break;
                }
            }

            switch (hostPolicyVersionInfo?.ProductMajorPart)
            {
                case 6:
                    return "net5.0-windows";

                case 5:
                    return "net5.0-windows";

                case 3 when hostPolicyVersionInfo.ProductMinorPart >= 1:
                    return "netcoreapp3.1";

                case 3:
                    return "netcoreapp3.0";

                default:
                    return "net451";
            }

        }
    }
}