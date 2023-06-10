using ScubaDiver.Rtti;

namespace ScubaDiver;

public abstract class UndecoratedFunction
{
    public string UndecoratedName { get; set; }
    public abstract string DecoratedName { get; }
    public abstract long Address { get; }
    public abstract ModuleInfo Module { get; }

    public UndecoratedFunction(string undecName)
    {
        UndecoratedName = undecName;
    }

    public override string ToString() => UndecoratedName;
}