using System;
using System.Linq;
using NtApiDotNet.Win32;
using ScubaDiver.Rtti;

namespace ScubaDiver;

public class UndecoratedExport : UndecoratedFunction
{
    private ModuleInfo _module;
    private Lazy<string[]> _lazyArgTypes;
    public override long Address => Export.Address;
    public override ModuleInfo Module => _module;
    public string ClassName { get; set; }
    public override string[] ArgTypes
    {
        get
        {
            string[] args = _lazyArgTypes.Value;
            if (args != null)
                args = args.Prepend(ClassName).ToArray(); // TODO: Assuming instance method
            return args;
        }
    }

    public override int? NumArgs => ArgTypes.Length;


    public DllExport Export { get; set; }

    public UndecoratedExport(string className, string undecoratedName, Lazy<string[]> args, DllExport export, ModuleInfo module) : base(export.Name, undecoratedName)
    {
        ClassName = className;
        UndecoratedName = undecoratedName;
        Export = export;
        _module = module;
        _lazyArgTypes = args;
    }
}