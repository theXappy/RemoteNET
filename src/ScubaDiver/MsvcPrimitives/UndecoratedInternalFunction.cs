using ScubaDiver.Rtti;

namespace ScubaDiver;

public class UndecoratedInternalFunction : UndecoratedFunction
{
    private ModuleInfo _module;
    private string _decoratedName;
    private long _address;

    public override string DecoratedName => _decoratedName;
    public override long Address => _address;
    public override ModuleInfo Module => _module;

    public UndecoratedInternalFunction(string undecoratedName, string decoratedName, long address, int numArgs, ModuleInfo moduleInfo) : base(undecoratedName, numArgs)
    {
        _address = address;
        _decoratedName = decoratedName + $" (INTERNAL)";
        _module = moduleInfo;
    }
}