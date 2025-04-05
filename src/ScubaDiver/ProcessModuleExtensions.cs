using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using ScubaDiver.Rtti;
using ScubaDiver;

static class ProcessModuleExtensions
{
    // Import the necessary Windows API functions
    [DllImport("kernel32.dll")]
    public static extern uint GetModuleFileName(IntPtr hModule, [Out] char[] lpFileName, int nSize);

    // Function to check if an IntPtr is valid
    public static bool IsIntPtrValid(IntPtr intPtr)
    {
        char[] buffer = new char[256]; // Adjust the buffer size as needed
        uint result = GetModuleFileName(intPtr, buffer, buffer.Length);

        // If GetModuleFileName returns 0, it means the IntPtr is invalid
        if (result == 0)
        {
            int error = Marshal.GetLastWin32Error();
            if (error == 126 /* ERROR_MOD_NOT_FOUND */ || error == 0)
            {
                return false;
            }
            else
            {
                Logger.Debug($"<GetLastWin32Error == {error}>");
            }
        }

        return true;
    }

    public static List<ModuleSection> ListSections(this ModuleInfo module)
    {
        // Get a pointer to the base address of the module
        IntPtr moduleHandle = new IntPtr((long)module.BaseAddress);

        if (!IsIntPtrValid(moduleHandle))
        {
            Logger.Debug($"[ListSections] == WARNING == Module unloaded! Name: {module.Name} Address: {moduleHandle}");
            return new List<ModuleSection>();
        }

        // Read the DOS header from the module
        IMAGE_DOS_HEADER dosHeader = Marshal.PtrToStructure<IMAGE_DOS_HEADER>(moduleHandle);

        // Read the PE header from the module
        IntPtr peHeader = new IntPtr(moduleHandle.ToInt64() + dosHeader.e_lfanew);
        IMAGE_NT_HEADERS ntHeaders = Marshal.PtrToStructure<IMAGE_NT_HEADERS>(peHeader);

        // Get a pointer to the section headers in the module
        IntPtr sectionHeaders = new IntPtr(peHeader.ToInt64() + Marshal.SizeOf(typeof(IMAGE_NT_HEADERS))) + 16;

        // Print the details of each section
        List<ModuleSection> output = new List<ModuleSection>();
        for (int i = 0; i < ntHeaders.FileHeader.NumberOfSections; i++)
        {
            // Read the section header from the module
            IntPtr sectionHeader = new IntPtr(sectionHeaders.ToInt64() + i * Marshal.SizeOf(typeof(IMAGE_SECTION_HEADER)));
            IMAGE_SECTION_HEADER section = Marshal.PtrToStructure<IMAGE_SECTION_HEADER>(sectionHeader);

            // Print the section details

            // To nearest page boundary
            uint alignment = (uint)ntHeaders.OptionalHeader.SectionAlignment;
            uint roundedVirtualSize = (section.VirtualSize + alignment - 1) & ~(alignment - 1);
            output.Add(
                new ModuleSection(Encoding.ASCII.GetString(section.Name).TrimEnd('\0'),
                    (ulong)module.BaseAddress + section.VirtualAddress,
                    roundedVirtualSize));
        }

        return output;
    }
}
