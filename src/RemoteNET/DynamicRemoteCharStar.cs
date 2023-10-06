using RemoteNET.Internal;
using System;
using System.Diagnostics;
using System.Dynamic;
using System.Linq;
using System.Reflection;

namespace RemoteNET;

[DebuggerDisplay("Dynamic Proxy of char* { Value: " + nameof(_innerString) + "}")]
public class DynamicRemoteCharStar : DynamicObject
{
    private string _innerString;

    public DynamicRemoteCharStar(string initialValue)
    {
        _innerString = initialValue;
    }

    public override bool TryGetMember(GetMemberBinder binder, out object result)
    {
        result = _innerString.GetType().GetProperty(binder.Name)?.GetValue(_innerString);
        if (result == null)
        {
            result = _innerString.GetType().GetField(binder.Name)?.GetValue(_innerString);
        }

        return result != null;
    }

    public override bool TrySetMember(SetMemberBinder binder, object value)
    {
        var propertyInfo = _innerString.GetType().GetProperty(binder.Name);
        if (propertyInfo != null)
        {
            propertyInfo.SetValue(_innerString, value);
            return true;
        }

        var fieldInfo = _innerString.GetType().GetField(binder.Name);
        if (fieldInfo != null)
        {
            fieldInfo.SetValue(_innerString, value);
            return true;
        }

        return false;
    }

    public override bool TryInvokeMember(InvokeMemberBinder binder, object[] args, out object result)
    {
        var methodName = binder.Name;
        var methodInfos = _innerString.GetType().GetMethods().Where(method => method.Name == methodName).ToList();

        if (methodInfos.Count == 0)
        {
            result = null;
            return false;
        }

        // Find the method with matching parameter types or compatible parameter types
        var argumentTypes = args.Select(arg => arg.GetType()).ToArray();
        var methodInfo = methodInfos.FirstOrDefault(m => ParametersMatch(m.GetParameters(), argumentTypes));

        if (methodInfo == null)
        {
            result = null;
            return false;
        }

        result = methodInfo.Invoke(_innerString, args);
        return true;
    }

    private static bool ParametersMatch(ParameterInfo[] parameters, Type[] argumentTypes)
    {
        if (parameters.Length != argumentTypes.Length)
        {
            return false;
        }

        for (int i = 0; i < parameters.Length; i++)
        {
            if (!parameters[i].ParameterType.IsAssignableFrom(argumentTypes[i]))
            {
                return false;
            }
        }

        return true;
    }

    public static implicit operator string(DynamicRemoteCharStar obj) => obj._innerString;


    #region ToString / GetHashCode / Equals

    public override string ToString()
    {
        return _innerString.ToString();
    }

    public override int GetHashCode()
    {
        // No "GetHashCode" method, target is not a .NET object
        // TODO: Return token?
        throw new NotImplementedException("Hashcode not implemented for non-managed objects");
    }
    public override bool Equals(object obj)
    {
        if (obj is DynamicRemoteCharStar drcs)
            return drcs._innerString == _innerString;
        return _innerString.Equals(obj);
    }
    #endregion
}