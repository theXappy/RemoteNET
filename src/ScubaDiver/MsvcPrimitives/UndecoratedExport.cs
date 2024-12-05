using System;
using System.Linq;
using NtApiDotNet.Win32;
using ScubaDiver.Rtti;
using static ScubaDiver.DllExportExt;

namespace ScubaDiver;

public class UndecoratedExportedFunc : UndecoratedFunction
{
    private ModuleInfo _module;
    private readonly Lazy<DemangledSignature> _lazySignatureParse;
    private string[] _lazyArgTypes => _lazySignatureParse.Value.ArgTypes;
    public override string RetType
    {
        get
        {
            // A non-ref return struct is actually compiled to a by-ref argument.
            // Return value should be the same as the Caller's provided by-ref arg (which is a pointer)
            if (_lazySignatureParse.Value.IsRetNonRefStruct)
            {
                return $"{_lazySignatureParse.Value.RetType}*";
            }
            return _lazySignatureParse.Value.RetType;
        }
    }

    public override long Address => Export.Address;
    public override ModuleInfo Module => _module;
    public string ClassName { get; set; }
    public override string[] ArgTypes
    {
        get
        {
            string[] args = _lazyArgTypes;

            // A non-ref return struct is actually compiled to a by-ref argument
            // e.g., Code: struct my_struct my_func(int x)
            // e.g., Decompiled: struct my_struct* my_func(struct my_struct* ret_value, int x);
            // See: https://learn.microsoft.com/en-us/cpp/build/x64-calling-convention?view=msvc-170
            if (_lazySignatureParse.Value.IsRetNonRefStruct)
                args = args.Prepend($"{_lazySignatureParse.Value.RetType}*").ToArray();

            if (args != null)
                args = args.Prepend($"{ClassName}*").ToArray(); // TODO: Assuming instance method

            return args;
        }
    }

    public override int? NumArgs => ArgTypes.Length;

    public DllExport Export { get; set; }

    public UndecoratedExportedFunc(string className, string undecoratedName, string undecoratedFullName, Lazy<DemangledSignature> signatureParser, DllExport export, ModuleInfo module) 
        : base(export.Name, undecoratedName, undecoratedFullName)
    {
        ClassName = className;
        Export = export;
        _module = module;
        _lazySignatureParse = signatureParser;
    }
}