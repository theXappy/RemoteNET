using System.Runtime.InteropServices;

namespace ScubaDiver
{
    [StructLayout(LayoutKind.Sequential)]
    struct IMAGE_NT_HEADERS
    {
        public int Signature;
        public IMAGE_FILE_HEADER FileHeader;
        public IMAGE_OPTIONAL_HEADER32 OptionalHeader;
    }
}
