using System;
using System.Linq;
using NtApiDotNet.Win32;
using ScubaDiver.Rtti;
using static ScubaDiver.DllExportExt;

namespace ScubaDiver;


public class UndecoratedExportedField : UndecoratedSymbol
{
    public const nuint XorMask = 0xaabbccdd; // Keeping it to 32 bits so it works in both x32 and x64
    public override nuint XoredAddress { get; }
    public override nuint Address => XoredAddress ^ XorMask;

    private ModuleInfo _module;
    public override ModuleInfo Module => _module;
    public DllExport Export { get; set; }

    public UndecoratedExportedField(nuint address, string undecoratedName, string undecoratedFullName, DllExport export, ModuleInfo module)
        : base(export.Name, undecoratedName, undecoratedFullName)
    {
        XoredAddress = address ^ XorMask;
        Export = export;
        _module = module;
    }

    public override string ToString() => $"Field: {UndecoratedFullName} 0x{Address:X16}";
}