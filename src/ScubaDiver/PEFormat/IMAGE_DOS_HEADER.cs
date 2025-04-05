using System.Runtime.InteropServices;

#pragma warning disable CS0649 // Field is never assigned to, and will always have its default value 0
namespace ScubaDiver
{
    [StructLayout(LayoutKind.Sequential)]
    struct IMAGE_DOS_HEADER
    {
        public short e_magic;
        public short e_cblp;
        public short e_cp;
        public short e_crlc;
        public short e_cparhdr;
        public short e_minalloc;
        public short e_maxalloc;
        public short e_ss;
        public short e_sp;
        public short e_csum;
        public short e_ip;
        public short e_cs;
        public short e_lfarlc;
        public short e_ovno;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 4)]
        public short[] e_res1;
        public short e_oemid;
        public short e_oeminfo;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 10)]
        public short[] e_res2;
        public int e_lfanew;
    }
}
#pragma warning restore CS0649 // Field 'IMAGE_DATA_DIRECTORY.Size' is never assigned to, and will always have its default value 0
