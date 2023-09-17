using System;
using System.Linq;
using System.Reflection;

namespace RemoteNET.Common;

public abstract class RemoteMethodInfoBase : MethodInfo
{
}

public interface IRttiMethodBase
{
    LazyRemoteTypeResolver LazyRetType { get; }
    /// <summary>
    /// All C++ parameters of the function. First one is (likely) 'this'
    /// </summary>
    ParameterInfo[] LazyParamInfos { get; }
    string Name { get; }

    public string UndecoratedSignature
    {
        get
        {
            string args = string.Join(", ", LazyParamInfos.Select(pi => pi.ToString()));
            return $"{LazyRetType.TypeFullName ?? LazyRetType.TypeName} {Name}({args})";
        }
    }
}