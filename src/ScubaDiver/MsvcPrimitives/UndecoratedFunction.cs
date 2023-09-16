using System.Linq;
using ScubaDiver.Rtti;

namespace ScubaDiver;

public abstract class UndecoratedSymbol
{
    public string UndecoratedName { get; set; }
    public string UndecoratedFullName { get; set; }
    public string DecoratedName { get; set; }
    public abstract long Address { get; }
    public abstract ModuleInfo Module { get; }

    public UndecoratedSymbol(string decoratedName, string undecName, string undecFullName)
    {
        DecoratedName = decoratedName;
        UndecoratedName = undecName;
        UndecoratedFullName = undecFullName;
    }

    public override int GetHashCode() => Address.GetHashCode();

    public override bool Equals(object obj)
    {
        if (obj is not UndecoratedFunction undec)
        {
            return false;
        }

        return Address == undec.Address &&
               DecoratedName == undec.DecoratedName;
    }
}

public abstract class UndecoratedFunction : UndecoratedSymbol
{
    public virtual string[] ArgTypes { get; }
    public virtual int? NumArgs { get; }
    public virtual string RetType { get; }

    public UndecoratedFunction(string decoratedName, string undecName, string undecFullName, int? numArgs = null)
        : base(decoratedName, undecName, undecFullName)
    {
        NumArgs = numArgs;
    }

    public override string ToString() => UndecoratedFullName;
}