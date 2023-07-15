using ScubaDiver.Rtti;

namespace ScubaDiver;

public abstract class UndecoratedFunction
{
    public string UndecoratedName { get; set; }
    public string UndecoratedFullName { get; set; }
    public string DecoratedName { get; set; }
    public abstract long Address { get; }
    public abstract ModuleInfo Module { get; }
    public virtual string[] ArgTypes { get; }
    public virtual int? NumArgs { get; }
    public virtual string RetType { get; }

    public UndecoratedFunction(string decoratedName, string undecName, string undecFullName, int? numArgs = null)
    {
        DecoratedName = decoratedName;
        UndecoratedName = undecName;
        UndecoratedFullName = undecFullName;
        NumArgs = numArgs;
    }

    public override string ToString() => UndecoratedFullName;
}