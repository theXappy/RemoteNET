namespace ScubaDiver;

public class UndecoratedInternalFunction : UndecoratedFunction
{
    private string _decoratedName;
    private long _address;

    public override string DecoratedName => _decoratedName;
    public override long Address => _address;

    public UndecoratedInternalFunction(string undecoratedName, string decoratedName, long address) : base(undecoratedName)
    {
        _address = address;
        _decoratedName = decoratedName;
    }
}