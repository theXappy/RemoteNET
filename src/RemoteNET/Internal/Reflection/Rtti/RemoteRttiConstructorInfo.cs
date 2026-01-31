using System;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Reflection;
using RemoteNET.Common;
using RemoteNET.RttiReflection;

namespace RemoteNET.Internal.Reflection;

[DebuggerDisplay("Remote RTTI Constructor: {LazyRetType.TypeFullName} {Name}(...)")]
public class RemoteRttiConstructorInfo : ConstructorInfo, IRttiMethodBase
{
    public LazyRemoteTypeResolver LazyRetType => new LazyRemoteTypeResolver(new DummyRttiType("void"));
    protected LazyRemoteParameterResolver[] _lazyParamInfosImpl;
    public LazyRemoteParameterResolver[] LazyParamInfos => _lazyParamInfosImpl;

    private MethodAttributes _attributes;
    public override MethodAttributes Attributes => _attributes;

    public override RuntimeMethodHandle MethodHandle => throw new NotImplementedException();

    protected LazyRemoteTypeResolver _lazyDeclaringType;
    public LazyRemoteTypeResolver LazyDeclaringType => _lazyDeclaringType;
    public override Type DeclaringType => LazyDeclaringType.Value;

    public override string Name => DeclaringType.Name;
    public string MangledName { get; }

    public override Type ReflectedType => throw new NotImplementedException();

    private RemoteApp App => (DeclaringType as RemoteRttiType)?.App;

    public RemoteRttiConstructorInfo(string mangledName, LazyRemoteTypeResolver declaringType, LazyRemoteParameterResolver[] paramInfos, MethodAttributes attributes)
    {
        MangledName = mangledName;
        _lazyDeclaringType = declaringType;
        _lazyParamInfosImpl = paramInfos;
        _attributes = attributes;
    }

    public override object[] GetCustomAttributes(bool inherit)
    {
        throw new NotImplementedException();
    }

    public override object[] GetCustomAttributes(Type attributeType, bool inherit)
    {
        throw new NotImplementedException();
    }

    public override MethodImplAttributes GetMethodImplementationFlags()
    {
        throw new NotImplementedException();
    }

    public override ParameterInfo[] GetParameters()
    {
        // (-1) because we're skipping 'this'
        ParameterInfo[] parameters = new ParameterInfo[_lazyParamInfosImpl.Length - 1];

        for (int i = 1; i < _lazyParamInfosImpl.Length; i++)
        {
            LazyRemoteParameterResolver lazyResolver = _lazyParamInfosImpl[i];
            parameters[i - 1] = new RemoteParameterInfo(lazyResolver.Name, lazyResolver.TypeResolver);
        }

        return parameters;
    }

    public override object Invoke(BindingFlags invokeAttr, Binder binder, object[] parameters, CultureInfo culture)
    {
        throw new NotImplementedException($"{nameof(RemoteRttiConstructorInfo)}.{nameof(Invoke)}");
        //return RemoteFunctionsInvokeHelper.Invoke(this.App, DeclaringType, Name, null, new Type[0], parameters);
    }

    public override object Invoke(object obj, BindingFlags invokeAttr, Binder binder, object[] parameters, CultureInfo culture)
    {
        return UnmanagedRemoteFunctionsInvokeHelper.Invoke(this.App as UnmanagedRemoteApp, DeclaringType, Name, obj, parameters);
    }

    public override bool IsDefined(Type attributeType, bool inherit)
    {
        throw new NotImplementedException();
    }

    public override string ToString()
    {
        string args = string.Join(", ", _lazyParamInfosImpl.Select(pi => pi.TypeResolver.TypeFullName));
        return $"Void {this.Name}({args})";
    }
}