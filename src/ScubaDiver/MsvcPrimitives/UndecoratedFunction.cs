using ScubaDiver.Rtti;

namespace ScubaDiver;

public abstract class UndecoratedSymbol
{
    public string UndecoratedName { get; set; }
    public string UndecoratedFullName { get; set; }
    public string DecoratedName { get; set; }
    public abstract ModuleInfo Module { get; }
    public abstract nuint XoredAddress { get; }
    public abstract nuint Address { get; }


    public UndecoratedSymbol(string decoratedName, string undecName, string undecFullName)
    {
        DecoratedName = decoratedName;
        UndecoratedName = undecName;
        UndecoratedFullName = undecFullName;
    }

    public override int GetHashCode() => XoredAddress.GetHashCode();

    public override bool Equals(object obj)
    {
        if (obj is not UndecoratedSymbol undec)
        {
            return false;
        }

        return XoredAddress == undec.XoredAddress &&
               DecoratedName == undec.DecoratedName;
    }
}

public abstract class UndecoratedFunction : UndecoratedSymbol
{
    public virtual string[] ArgTypes { get; }
    public virtual int? NumArgs { get; }
    public virtual string RetType { get; }

    // I think we can get away with not xoring the address for functions
    public override nuint XoredAddress => Address;

    public UndecoratedFunction(string decoratedName, string undecName, string undecFullName, int? numArgs = null)
        : base(decoratedName, undecName, undecFullName)
    {
        NumArgs = numArgs;
    }

    public override string ToString() => $"Func: " + UndecoratedFullName;
}