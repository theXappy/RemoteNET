using System.Collections.Generic;

namespace ScubaDiver;

public class UndecoratedType : Dictionary<string, UndecoratedMethodGroup>
{
    public void AddOrCreate(string methodName, UndecoratedFunction func) => GetOrAddGroup(methodName).Add(func);

    private UndecoratedMethodGroup GetOrAddGroup(string method)
    {
        if (!ContainsKey(method))
            this[method] = new UndecoratedMethodGroup();
        return this[method];
    }
}