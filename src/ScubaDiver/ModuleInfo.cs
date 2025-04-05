namespace ScubaDiver.Rtti;

using System;
using System.Diagnostics.CodeAnalysis;

public struct ModuleInfo
{
    public string Name;
    public nuint BaseAddress;
    public nuint Size;
    public ModuleInfo(string name, nuint baseAddress, nuint size)
    {
        Name = name;
        BaseAddress = baseAddress;
        Size = size;
    }

    public override string ToString() => $"{Name} 0x({BaseAddress:x8}, {Size} bytes)";

    public override bool Equals(object obj)
    {
        return obj is ModuleInfo info &&
               Name == info.Name &&
               BaseAddress == info.BaseAddress &&
               Size == info.Size;
    }

    public override int GetHashCode()
    {
        int hash = 17;
        hash = hash * 31 + (Name?.GetHashCode() ?? 0);
        hash = hash * 31 + BaseAddress.GetHashCode();
        hash = hash * 31 + Size.GetHashCode();
        return hash;
    }
}
