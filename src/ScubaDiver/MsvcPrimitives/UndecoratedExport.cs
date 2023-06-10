using NtApiDotNet.Win32;
using ScubaDiver.Rtti;

namespace ScubaDiver;

public class UndecoratedExport : UndecoratedFunction
{
    private ModuleInfo _module;
    public override string DecoratedName => Export.Name;
    public override long Address => Export.Address;
    public override ModuleInfo Module => _module;
    public DllExport Export { get; set; }

    public UndecoratedExport(string undecoratedName, DllExport export, ModuleInfo module) : base(undecoratedName)
    {
        UndecoratedName = undecoratedName;
        Export = export;
        _module = module;
    }
}