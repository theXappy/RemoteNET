using System;
using System.Globalization;
using System.Linq;
using System.Reflection;
using ScubaDiver;
using ScubaDiver.API;
using ScubaDiver.API.Dumps;
using ScubaDiver.API.Extensions;
using ScubaDiver.API.Utils;

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

        private RemoteApp App => (DeclaringType as RemoteType)?.App;

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

            ObjectOrRemoteAddress[] remoteParams = new ObjectOrRemoteAddress[parameters.Length];
            for (int i = 0; i < parameters.Length; i++)
            {
                object parameter = parameters[i];
                if (parameter.GetType().IsPrimitiveEtc())
                {
                    remoteParams[i] = ObjectOrRemoteAddress.FromObj(parameter);
                }
                else if (parameter is RemoteObject remoteArg)
                {
                    remoteParams[i] =
                        ObjectOrRemoteAddress.FromToken(remoteArg.RemoteToken, remoteArg.GetType().FullName);
                }
                else if (parameter is DynamicRemoteObject dro)
                {
                    RemoteObject originRemoteObject = dro.__ro;
                    remoteParams[i] = ObjectOrRemoteAddress.FromToken(originRemoteObject.RemoteToken, originRemoteObject.GetType().FullName);
                }
                else
                {
                    throw new Exception(
                        $"{nameof(RemoteMethodInfo)}.{nameof(Invoke)} only works with primitive (int, " +
                        $"double, string,...) or remote (in {nameof(RemoteObject)}) parameters. " +
                        $"The parameter at index {i} was of unsupported type {parameters.GetType()}");
                }
            }

            bool hasResults;
            ObjectOrRemoteAddress oora;
            if (obj == null)
            {
                if (this.App == null)
                {
                    throw new InvalidOperationException($"Trying to invoke a static call (null target object) " +
                                                        $"on a {nameof(RemoteMethodInfo)} but it's associated " +
                                                        $"Declaring Type ({this.DeclaringType}) does not have a RemoteApp associated. " +
                                                        $"The type was either mis-constructed or it's not a {nameof(RemoteType)} object");
                }

                InvocationResults invokeRes = this.App.Communicator.InvokeStaticMethod(DeclaringType.FullName, this.Name, remoteParams);
                if (invokeRes.VoidReturnType)
                {
                    hasResults = false;
                    oora = null;
                }
                else
                {
                    hasResults = true;
                    oora = invokeRes.ReturnedObjectOrAddress;
                }
            }
            else
            {
                // obj is NOT null. Make sure it's a RemoteObject.
                if (!(obj is RemoteObject ro))
                {
                    throw new NotImplementedException(
                        $"{nameof(RemoteMethodInfo)}.{nameof(Invoke)} only supports {nameof(RemoteObject)} targets at the moment.");
                }
                (hasResults, oora) = ro.InvokeMethod(this.Name, remoteParams);
            }

            if (!hasResults)
                return null;

            // Non-void function.
            if (oora.IsNull)
                return null;
            if(!oora.IsRemoteAddress)
            {
                return PrimitivesEncoder.Decode(oora);
            }
            else
            {
                RemoteObject ro = App.GetRemoteObject(oora.RemoteAddress);
                return ro.Dynamify();
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

        public override string ToString()
        {
            string args = string.Join(", ", _paramInfos.Select(pi => pi.ParameterType.FullName));
            return $"{this.ReturnType.FullName} {this.Name}({args})";
        }
    }
}