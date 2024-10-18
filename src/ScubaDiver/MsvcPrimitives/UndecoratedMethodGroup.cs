using System.Collections.Generic;

namespace ScubaDiver;

public class UndecoratedMethodGroup : List<UndecoratedFunction>
{
    public string Name;
    public UndecoratedMethodGroup(string name)
    {
        Name = name;
    }
}