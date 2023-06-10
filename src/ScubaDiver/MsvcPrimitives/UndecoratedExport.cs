using NtApiDotNet.Win32;

namespace ScubaDiver;

public class UndecoratedExport : UndecoratedFunction
{
    public override string DecoratedName => Export.Name;
    public override long Address => Export.Address;
    public DllExport Export { get; set; }

    public UndecoratedExport(string undecoratedName, DllExport export) : base(undecoratedName)
    {
        UndecoratedName = undecoratedName;
        Export = export;
    }
}