using System.Collections.Generic;

namespace ScubaDiver.Rtti;

public class ModuleOperatorFunctions
{
    public List<nuint> OperatorNewFuncs { get; } = new();
    public List<nuint> OperatorDeleteFuncs { get; } = new();
}
