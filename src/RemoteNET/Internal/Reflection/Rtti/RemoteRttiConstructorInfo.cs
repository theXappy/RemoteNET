using System;
using System.Globalization;
using System.Linq;
using System.Reflection;
using RemoteNET.Common;
using RemoteNET.RttiReflection;

namespace RemoteNET.Internal.Reflection;

public class RemoteRttiConstructorInfo : ConstructorInfo
{
    public override MethodAttributes Attributes => throw new NotImplementedException();

    public override RuntimeMethodHandle MethodHandle => throw new NotImplementedException();

    public override Type DeclaringType { get; }

    public override string Name => ".ctor";

    public override Type ReflectedType => throw new NotImplementedException();

    private readonly ParameterInfo[] _paramInfos;
    private RemoteApp App => (DeclaringType as RemoteRttiType)?.App;

    public RemoteRttiConstructorInfo(Type declaringType, ParameterInfo[] paramInfos)
    {
        DeclaringType = declaringType;
        _paramInfos = paramInfos;
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

    public override ParameterInfo[] GetParameters() => _paramInfos;

    public override object Invoke(BindingFlags invokeAttr, Binder binder, object[] parameters, CultureInfo culture)
    {
        throw new NotImplementedException($"{nameof(RemoteRttiConstructorInfo)}.{nameof(Invoke)}");
        //return RemoteFunctionsInvokeHelper.Invoke(this.App, DeclaringType, Name, null, new Type[0], parameters);
    }

    public override object Invoke(object obj, BindingFlags invokeAttr, Binder binder, object[] parameters, CultureInfo culture)
    {
        // Empiricly it seems that invoking a ctor on an existing object should return null.
        if (obj == null)
        {
            // Last chace - If this overload was used but no real object given lets redirect to normal Invoke (this also happens with norma 'ConstructorInfo's)
            return Invoke(invokeAttr, binder, parameters, culture);
        }
        return null;
    }

    public override bool IsDefined(Type attributeType, bool inherit)
    {
        throw new NotImplementedException();
    }

    public override string ToString()
    {
        string args = string.Join(", ", _paramInfos.Select(pi => pi.ParameterType.FullName));
        return $"Void {this.Name}({args})";
    }
}