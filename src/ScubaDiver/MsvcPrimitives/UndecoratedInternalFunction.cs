using ScubaDiver.Rtti;

namespace ScubaDiver;

public class UndecoratedInternalFunction : UndecoratedFunction
{
    private ModuleInfo _module;
    private long _address;

    public override long Address => _address;
    public override ModuleInfo Module => _module;

    public UndecoratedInternalFunction(string undecoratedName, string decoratedName, long address, int numArgs, ModuleInfo moduleInfo) : base(decoratedName, undecoratedName, numArgs)
    {
        _address = address;
        _module = moduleInfo;
    }
}