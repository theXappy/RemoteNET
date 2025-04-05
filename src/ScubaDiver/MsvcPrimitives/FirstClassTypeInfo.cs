using System.Collections.Generic;
using System.Linq;

namespace ScubaDiver.Rtti;

/// <summary>
/// Information about a "First-Class Type" - Types which have a full RTTI entry and, most importantly, a vftable.
/// </summary>
public class FirstClassTypeInfo : TypeInfo
{
    public const nuint XorMask = 0xaabbccdd; // Keeping it to 32 bits so it works in both x32 and x64
    public nuint XoredVftableAddress { get; }
    public nuint VftableAddress => XoredVftableAddress ^ XorMask; // A function-based property so the address isn't needlessly kept in memory.
    public nuint Offset { get; }
    public List<nuint> XoredSecondaryVftableAddresses { get; }
    public IEnumerable<nuint> SecondaryVftableAddresses => XoredSecondaryVftableAddresses.Select(x => x ^ XorMask);

    public FirstClassTypeInfo(string moduleName, string @namespace, string name, nuint VftableAddress, nuint Offset) : base(moduleName, @namespace, name)
    {
        XoredVftableAddress = VftableAddress ^ XorMask;
        this.Offset = Offset;
        XoredSecondaryVftableAddresses = new List<nuint>();
    }

    public void AddSecondaryVftable(nuint vftableAddress)
    {
        XoredSecondaryVftableAddresses.Add(vftableAddress ^ XorMask);
    }

    public bool CompareXoredMethodTable(nuint xoredValue, nuint xoredMask)
    {
        return XoredVftableAddress == xoredValue &&
               XorMask == xoredMask;
    }

    public override string ToString()
    {
        return $"{Name} (First Class Type) ({Offset:X16})";
    }
}
