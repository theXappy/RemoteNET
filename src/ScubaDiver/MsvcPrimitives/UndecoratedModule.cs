using System.Collections.Generic;
using System.Diagnostics;
using ScubaDiver.Rtti;

namespace ScubaDiver;

[DebuggerDisplay("{Name} (UndecoratedModule)")]
public class UndecoratedModule
{
    public string Name { get; private set; }
    public Rtti.ModuleInfo ModuleInfo { get; private set; }

    private Dictionary<string, Rtti.TypeInfo> _namesToTypes;
    private Dictionary<Rtti.TypeInfo, UndecoratedType> _types;
    private Dictionary<string, UndecoratedMethodGroup> _typelessFunctions;


    public UndecoratedModule(string name, Rtti.ModuleInfo moduleInfo)
    {
        Name = name;
        _namesToTypes = new Dictionary<string, TypeInfo>();
        _types = new Dictionary<Rtti.TypeInfo, UndecoratedType>();
        _typelessFunctions = new Dictionary<string, UndecoratedMethodGroup>();
        ModuleInfo = moduleInfo;
    }

    public IEnumerable<Rtti.TypeInfo> Types => _types.Keys;

    public bool TryGetType(Rtti.TypeInfo type, out UndecoratedType res)
        => _types.TryGetValue(type, out res);
    public bool TryGetType(string name, out UndecoratedType res)
        => _namesToTypes.TryGetValue(name, out TypeInfo type) & _types.TryGetValue(type, out res);


    public void AddTypeFunction(Rtti.TypeInfo type, UndecoratedFunction func)
    {
        UndecoratedType undType = GetOrAddType(type);
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

    public UndecoratedType GetOrAddType(Rtti.TypeInfo type)
    {
        if(!_namesToTypes.ContainsKey(type.Name))
            _namesToTypes[type.Name] = type;

        if (!_types.ContainsKey(type))
            _types[type] = new UndecoratedType();
        return _types[type];
    }
}