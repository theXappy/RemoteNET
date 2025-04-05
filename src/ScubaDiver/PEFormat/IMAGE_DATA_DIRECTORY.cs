// X is never assigned to, and will always have its default value 0
#pragma warning disable CS0649
namespace ScubaDiver
{
    struct IMAGE_DATA_DIRECTORY
    {
        public uint VirtualAddress;
        public uint Size;
    }
}
