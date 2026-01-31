using System;
using System.Linq;
using System.Reflection;

namespace RemoteNET.Common;

public abstract class RemoteMethodInfoBase : MethodInfo
{
}

public interface IRttiMethodBase
{
    LazyRemoteTypeResolver LazyDeclaringType { get; }


    LazyRemoteTypeResolver LazyRetType { get; }
    /// <summary>
    /// All C++ parameters of the function. First one is (likely) 'this'
    /// </summary>
    LazyRemoteParameterResolver[] LazyParamInfos { get; }
    string Name { get; }
    string MangledName { get; }

    public string UndecoratedSignature
    {
        get
        {
            string args = string.Join(", ", LazyParamInfos.Select(resolver => resolver.ToString()));
            return $"{LazyRetType.TypeFullName ?? LazyRetType.TypeName} {Name}({args})";
        }
    }
}