using ScubaDiver.Rtti;

namespace ScubaDiver;

public abstract class UndecoratedFunction
{
    public string UndecoratedName { get; set; }
    public abstract string DecoratedName { get; }
    public abstract long Address { get; }
    public abstract ModuleInfo Module { get; }
    public virtual int? NumArgs { get; }

    public UndecoratedFunction(string undecName, int? numArgs = null)
    {
        UndecoratedName = undecName;
        NumArgs = numArgs;
    }

    public override string ToString() => UndecoratedName;
}