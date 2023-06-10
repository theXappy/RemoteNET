namespace ScubaDiver;

public abstract class UndecoratedFunction
{
    public string UndecoratedName { get; set; }
    public abstract string DecoratedName { get; }
    public abstract long Address { get; }

    public UndecoratedFunction(string undecName)
    {
        UndecoratedName = undecName;
    }

    public override string ToString() => UndecoratedName;
}