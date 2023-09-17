using System;
using System.Globalization;
using System.Linq;
using System.Reflection;
using RemoteNET.Common;
using RemoteNET.RttiReflection;

namespace RemoteNET.Internal.Reflection;

public class RemoteRttiConstructorInfo : ConstructorInfo, IRttiMethodBase
{
    public LazyRemoteTypeResolver LazyRetType => new LazyRemoteTypeResolver(typeof(void));
    protected ParameterInfo[] _lazyParamInfosImpl;
    public ParameterInfo[] LazyParamInfos => _lazyParamInfosImpl;

    public override MethodAttributes Attributes => throw new NotImplementedException();

    public override RuntimeMethodHandle MethodHandle => throw new NotImplementedException();

    public override Type DeclaringType { get; }

    public override string Name => DeclaringType.Name;

    public override Type ReflectedType => throw new NotImplementedException();

    private RemoteApp App => (DeclaringType as RemoteRttiType)?.App;

    public RemoteRttiConstructorInfo(Type declaringType, ParameterInfo[] paramInfos)
    {
        DeclaringType = declaringType;
        _lazyParamInfosImpl = paramInfos;
    }

    public RemoteRttiConstructorInfo(RemoteRttiType declaringType, ConstructorInfo ci) :
        this(declaringType,
            ci.GetParameters().Select(pi => new RemoteParameterInfo(pi)).Cast<ParameterInfo>().ToArray())
    {
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
        // Skipping 'this'
        return _lazyParamInfosImpl.Skip(1).ToArray();
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
        string args = string.Join(", ", _lazyParamInfosImpl.Select(pi => pi.ParameterType.FullName));
        return $"Void {this.Name}({args})";
    }
}