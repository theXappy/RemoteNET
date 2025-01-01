using ScubaDiver.Rtti;
using System;
using System.Linq;

namespace ScubaDiver;

public class UndecoratedInternalFunction : UndecoratedFunction
{
    private ModuleInfo _module;
    private long _address;

    public override long Address => _address;
    public override ModuleInfo Module => _module;

    private string[] _argTypes;
    public override string[] ArgTypes => _argTypes;

    public UndecoratedInternalFunction(
        string undecoratedName, 
        string undecoratedFullName, 
        string decoratedName, 
        long address, 
        int numArgs, 
        ModuleInfo moduleInfo) 
        : base(decoratedName, undecoratedName, undecoratedFullName, numArgs)
    {
        _address = address;
        _module = moduleInfo;

        _argTypes = Enumerable.Repeat("long", numArgs).ToArray();
    }
}