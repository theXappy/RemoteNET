namespace ScubaDiver.Rtti;

public class ModuleSection
{
    public string Name { get; private set; }
    public ulong BaseAddress { get; private set; }
    public ulong Size { get; private set; }

    public ModuleSection(string name, ulong baseAddress, ulong size)
    {
        Name = name;
        BaseAddress = baseAddress;
        Size = size;
    }

    public override string ToString()
    {
        return string.Format("{0,-8}: 0x{1:X8} - 0x{2:X8} (0x{3:X8} bytes)",
            Name,
            BaseAddress,
            BaseAddress + Size,
            Size);
    }
}
