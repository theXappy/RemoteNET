using System.Collections.Generic;

namespace ScubaDiver;

public class UndecoratedModule
{
    public string Name { get; private set; }
    public Rtti.ModuleInfo ModuleInfo { get; private set; }

    private Dictionary<Rtti.TypeInfo, UndecoratedType> _types;
    private Dictionary<string, UndecoratedMethodGroup> _typelessFunctions;


    public UndecoratedModule(string name, Rtti.ModuleInfo moduleInfo)
    {
        Name = name;
        _types = new Dictionary<Rtti.TypeInfo, UndecoratedType>();
        _typelessFunctions = new Dictionary<string, UndecoratedMethodGroup>();
        ModuleInfo = moduleInfo;
    }

    public IEnumerable<Rtti.TypeInfo> Types => _types.Keys;

    public bool TryGetType(Rtti.TypeInfo type, out UndecoratedType res)
        => _types.TryGetValue(type, out res);


    public void AddTypeFunction(Rtti.TypeInfo type, UndecoratedFunction func)
    {
        var undType = GetOrAdd(type);
        if (!undType.ContainsKey(func.UndecoratedFullName))
            undType[func.UndecoratedFullName] = new UndecoratedMethodGroup();
        undType[func.UndecoratedFullName].Add(func);
    }

    public bool TryGetTypeFunc(Rtti.TypeInfo type, string undecMethodName, out UndecoratedMethodGroup res)
    {
        res = null;
        return TryGetType(type, out var undType) && undType.TryGetValue(undecMethodName, out res);
    }
    public void AddTypelessFunction(UndecoratedFunction func)
    {
        string decoratedMethodName = func.DecoratedName;
        if (!_typelessFunctions.ContainsKey(decoratedMethodName))
            _typelessFunctions[decoratedMethodName] = new UndecoratedMethodGroup();
        _typelessFunctions[decoratedMethodName].Add(func);
    }
    public bool TryGetTypelessFunc(string decoratedMethodName, out UndecoratedMethodGroup res)
    {
        return _typelessFunctions.TryGetValue(decoratedMethodName, out res);
    }

    private UndecoratedType GetOrAdd(Rtti.TypeInfo type)
    {
        if (!_types.ContainsKey(type))
            _types[type] = new UndecoratedType();
        return _types[type];
    }
}