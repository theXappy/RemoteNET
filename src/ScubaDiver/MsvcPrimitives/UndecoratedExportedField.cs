using System;
using System.Linq;
using NtApiDotNet.Win32;
using ScubaDiver.Rtti;
using static ScubaDiver.DllExportExt;

namespace ScubaDiver;


public class UndecoratedExportedField : UndecoratedSymbol
{
    private long _address;
    public override long Address => _address;
    private ModuleInfo _module;
    public override ModuleInfo Module => _module;
    public DllExport Export { get; set; }

    public UndecoratedExportedField(long address, string undecoratedName, string undecoratedFullName, DllExport export, ModuleInfo module)
        : base(export.Name, undecoratedName, undecoratedFullName)
    {
        _address = address;
        Export = export;
        _module = module;
    }
}