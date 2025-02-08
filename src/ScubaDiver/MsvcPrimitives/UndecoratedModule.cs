using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using NtApiDotNet.Win32;
using ScubaDiver.Rtti;

namespace ScubaDiver;

[DebuggerDisplay("{Name} (UndecoratedModule)")]
public class UndecoratedModule
{
    public string Name { get; private set; }
    public Rtti.RichModuleInfo RichModule { get; private set; }
    public Rtti.ModuleInfo ModuleInfo { get; private set; }

    private Dictionary<string, Rtti.TypeInfo> _namesToTypes;
    private Dictionary<Rtti.TypeInfo, UndecoratedType> _types;
    private Dictionary<string, UndecoratedMethodGroup> _undecoratedTypelessFunctions;
    private Dictionary<string, List<DllExport>> _leftoverTypelessFunctions;

    public UndecoratedModule(string name, Rtti.RichModuleInfo richModule)
    {
        Name = name;
        _namesToTypes = new Dictionary<string, TypeInfo>();
        _types = new Dictionary<Rtti.TypeInfo, UndecoratedType>();
        _undecoratedTypelessFunctions = new Dictionary<string, UndecoratedMethodGroup>();
        _leftoverTypelessFunctions = new Dictionary<string, List<DllExport>>();
        RichModule = richModule;
        ModuleInfo = richModule.ModuleInfo;
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
            undType[func.UndecoratedFullName] = new UndecoratedMethodGroup(func.UndecoratedFullName);
        undType[func.UndecoratedFullName].Add(func);
    }

    public bool TryGetTypeFunc(Rtti.TypeInfo type, string undecMethodName, out UndecoratedMethodGroup res)
    {
        res = null;
        return TryGetType(type, out var undType) && undType.TryGetValue(undecMethodName, out res);
    }

    public void AddRegularTypelessFunction(DllExport func)
    {
        if (!_leftoverTypelessFunctions.ContainsKey(func.Name))
            _leftoverTypelessFunctions[func.Name] = new List<DllExport>();
        _leftoverTypelessFunctions[func.Name].Add(func);
    }

    public bool TryGetRegularTypelessFunc(string name, out List<DllExport> res)
    {
        return _leftoverTypelessFunctions.TryGetValue(name, out res);
    }
    public IEnumerable<DllExport> GetRegularTypelessFuncs()
    {
        return _leftoverTypelessFunctions.Values.SelectMany(x => x);
    }

    public void AddUndecoratedTypelessFunction(UndecoratedFunction func)
    {
        string decoratedMethodName = func.DecoratedName;
        if (!_undecoratedTypelessFunctions.ContainsKey(decoratedMethodName))
            _undecoratedTypelessFunctions[decoratedMethodName] = new UndecoratedMethodGroup(decoratedMethodName);
        _undecoratedTypelessFunctions[decoratedMethodName].Add(func);
    }

    public bool TryGetUndecoratedTypelessFunc(string name, out UndecoratedMethodGroup res)
    {
        return _undecoratedTypelessFunctions.TryGetValue(name, out res);
    }
    public IEnumerable<UndecoratedMethodGroup> GetUndecoratedTypelessFuncs()
    {
        return _undecoratedTypelessFunctions.Values;        
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