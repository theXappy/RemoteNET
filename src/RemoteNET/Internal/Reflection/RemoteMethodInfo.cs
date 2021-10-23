using System;
using System.Globalization;
using System.Reflection;
using ScubaDiver;
using ScubaDiver.Extensions;

namespace RemoteNET.Internal.Reflection
{
    public class RemoteMethodInfo : MethodInfo
    {
        public override ICustomAttributeProvider ReturnTypeCustomAttributes => throw new NotImplementedException();
        public override string Name { get; }
        public override Type DeclaringType { get; }
        public override Type ReturnType { get; }
        public override Type ReflectedType => throw new NotImplementedException();
        public override RuntimeMethodHandle MethodHandle => throw new NotImplementedException();
        public override MethodAttributes Attributes => throw new NotImplementedException();

        private ParameterInfo[] _paramInfos;

        public RemoteMethodInfo(Type declaringType, Type returnType, string name, ParameterInfo[] paramInfos)
        {
            Name = name;
            DeclaringType = declaringType;
            _paramInfos = paramInfos;
            ReturnType = returnType;
        }

        public override object[] GetCustomAttributes(bool inherit)
        {
            throw new NotImplementedException();
        }

        public override bool IsDefined(Type attributeType, bool inherit)
        {
            throw new NotImplementedException();
        }

        public override ParameterInfo[] GetParameters() => _paramInfos;

        public override MethodImplAttributes GetMethodImplementationFlags()
        {
            throw new NotImplementedException();
        }

        public override object Invoke(object obj, BindingFlags invokeAttr, Binder binder, object[] parameters, CultureInfo culture)
        {
            // invokeAttr, binder and culture currently ignored

            if (obj == null)
            {
                // TODO: support static calls
                throw new NotImplementedException("Static calls (where no object is provided) are not yet supported.");
            }

            if (obj is RemoteObject ro)
            {
                ObjectOrRemoteAddress[] remoteParams = new ObjectOrRemoteAddress[parameters.Length];
                for(int i=0;i<parameters.Length;i++)
                {
                    object parameter = parameters[i];
                    if (parameter.GetType().IsPrimitiveEtc())
                    {
                        remoteParams[i] = ObjectOrRemoteAddress.FromObj(parameter);
                    }
                    else if(parameter is RemoteObject remoteArg)
                    {
                        remoteParams[i] = ObjectOrRemoteAddress.FromToken(remoteArg.RemoteToken, remoteArg.GetType().FullName);
                    }
                    else
                    {
                        throw new Exception($"{nameof(RemoteMethodInfo)}.{nameof(Invoke)} only works with primitive (int, " +
                                            $"double, string,...) or remote (in {nameof(RemoteObject)}) parameters. " +
                                            $"The parameter at index {i} was of unsupported type {parameters.GetType()}");
                    }
                }
                return ro.InvokeMethod(this.Name, remoteParams);
            }
            else
            {
                throw new NotImplementedException(
                    $"{nameof(RemoteMethodInfo)}.{nameof(Invoke)} only supports {nameof(RemoteObject)} targets at the moment.");
            }
        }

        public override MethodInfo GetBaseDefinition()
        {
            throw new NotImplementedException();
        }

        public override object[] GetCustomAttributes(Type attributeType, bool inherit)
        {
            throw new NotImplementedException();
        }
    }
}