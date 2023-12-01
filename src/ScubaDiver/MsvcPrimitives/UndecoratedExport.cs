using System;
using System.Linq;
using NtApiDotNet.Win32;
using ScubaDiver.Rtti;

namespace ScubaDiver;

public class UndecoratedExportedFunc : UndecoratedFunction
{
    private ModuleInfo _module;
    private Lazy<(string, string[])> _lazySignatureParse;
    private string[] _lazyArgTypes => _lazySignatureParse.Value.Item2;
    public override string RetType => _lazySignatureParse.Value.Item1;
    public override long Address => Export.Address;
    public override ModuleInfo Module => _module;
    public string ClassName { get; set; }
    public override string[] ArgTypes
    {
        get
        {
            string[] args = _lazyArgTypes;
            if (args != null)
                args = args.Prepend($"{ClassName}*").ToArray(); // TODO: Assuming instance method
            return args;
        }
    }

    public override int? NumArgs => ArgTypes.Length;

    public DllExport Export { get; set; }

    public UndecoratedExportedFunc(string className, string undecoratedName, string undecoratedFullName, Lazy<(string, string[])> signatureParser, DllExport export, ModuleInfo module) 
        : base(export.Name, undecoratedName, undecoratedFullName)
    {
        ClassName = className;
        Export = export;
        _module = module;
        _lazySignatureParse = signatureParser;
    }
}

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