using System;
using NtApiDotNet.Win32;
using ScubaDiver.Rtti;

namespace ScubaDiver;

public class UndecoratedExport : UndecoratedFunction
{
    private ModuleInfo _module;
    private Lazy<int?> _lazyNumArgs;
    public override string DecoratedName => Export.Name;
    public override long Address => Export.Address;
    public override ModuleInfo Module => _module;
    public override int? NumArgs
    {
        get
        {
            int? res = _lazyNumArgs.Value;
            if (res != null)
                res++; // TODO: Assuming instance method
            return res;
        }
    }

    public DllExport Export { get; set; }

    public UndecoratedExport(string undecoratedName, Lazy<int?> numArgs, DllExport export, ModuleInfo module) : base(undecoratedName)
    {
        UndecoratedName = undecoratedName;
        Export = export;
        _module = module;
        _lazyNumArgs = numArgs;
    }
}