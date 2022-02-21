using System;
using System.Globalization;
using System.Reflection;
using ScubaDiver.API;
using ScubaDiver.API.Utils;

namespace RemoteNET.Internal.Reflection
{
    public class RemoteFieldInfo : FieldInfo
    {
        private RemoteApp App => (DeclaringType as RemoteType)?.App;
        public override object[] GetCustomAttributes(bool inherit)
        {
            throw new NotImplementedException();
        }

        public override object[] GetCustomAttributes(Type attributeType, bool inherit)
        {
            throw new NotImplementedException();
        }

        public override bool IsDefined(Type attributeType, bool inherit)
        {
            throw new NotImplementedException();
        }

        public override Type FieldType { get; }
        public override Type DeclaringType { get; }
        public override string Name { get; }

        public RemoteFieldInfo(Type declaringType, Type fieldType, string name)
        {
            FieldType = fieldType;
            DeclaringType = declaringType;
            Name = name;
        }

        public override Type ReflectedType { get; }
        public override object GetValue(object obj)
        {
            ObjectOrRemoteAddress oora = null;
            if (obj == null)
            {
                // No 'this' object --> Static field

                if (this.App == null)
                {
                    throw new InvalidOperationException($"Trying to get a static field (null target object) " +
                                                        $"on a {nameof(RemoteFieldInfo)} but it's associated " +
                                                        $"Declaring Type ({this.DeclaringType}) does not have a RemoteApp associated. " +
                                                        $"The type was either mis-constructed or it's not a {nameof(RemoteType)} object");
                }

                oora = this.App.Communicator.GetField(0, DeclaringType.FullName, this.Name).ReturnedObjectOrAddress;
            }
            else
            {
                // obj is NOT null. Make sure it's a RemoteObject or DynamicRemoteObject.
                RemoteObject ro = obj as RemoteObject;
                ro ??= (obj as DynamicRemoteObject)?.__ro;
                if (ro != null)
                {
                    oora = ro.GetField(this.Name);
                }
                else
                {
                    throw new NotImplementedException(
                        $"{nameof(RemoteFieldInfo)}.{nameof(GetValue)} only supports {nameof(RemoteObject)} or {nameof(DynamicRemoteObject)} targets.");
                }
            }

            if (oora == null)
            {
                string offendingFunc = obj == null ? $"{nameof(DiverCommunicator)}.{nameof(DiverCommunicator.GetField)}" : $"{nameof(RemoteObject)}.{nameof(RemoteObject.GetField)}";
                throw new Exception($"Could not get {nameof(ObjectOrRemoteAddress)} object. Seems like invoking {offendingFunc} returned null.");
            }
            else
            {
                if (oora.IsRemoteAddress)
                {
                    var remoteObject = this.App.GetRemoteObject(oora.RemoteAddress);
                    return remoteObject.Dynamify();
                }
                else if (oora.IsNull)
                {
                    return null;
                }
                // Primitive
                return PrimitivesEncoder.Decode(oora.EncodedObject, oora.Type);
            }
        }

        public override void SetValue(object obj, object value, BindingFlags invokeAttr, Binder binder, CultureInfo culture)
        {
            var val = value;
            if (val.GetType().IsEnum)
            {
                var enumClass = this.App.GetRemoteEnum(val.GetType().FullName);
                // TODO: This will break on the first enum value which represents 2 or more flags
                object enumVal = enumClass.GetValue(val.ToString());
                // NOTE: Object stays in place in the remote app as long as we have it's reference
                // in the the value variable(so untill end of this method)
                value = enumVal;
            }

            // Might throw if the parameter is a local object (not RemoteObject or DynamicRemoteObject).
            ObjectOrRemoteAddress remoteNewValue = RemoteFunctionsInvokeHelper.CreateRemoteParameter(value);

            if (obj == null)
            {
                // No 'this' object --> Static field

                if (this.App == null)
                {
                    throw new InvalidOperationException($"Trying to get a static field (null target object) " +
                                                        $"on a {nameof(RemoteFieldInfo)} but it's associated " +
                                                        $"Declaring Type ({this.DeclaringType}) does not have a RemoteApp associated. " +
                                                        $"The type was either mis-constructed or it's not a {nameof(RemoteType)} object");
                }

                this.App.Communicator.SetField(0, DeclaringType.FullName, this.Name, remoteNewValue);
                return;
            }

            // obj is NOT null. Make sure it's a RemoteObject or DynamicRemoteObject.
            RemoteObject ro = obj as RemoteObject;
            ro ??= (obj as DynamicRemoteObject)?.__ro;
            if (ro != null)
            {
                ro.SetField(this.Name, remoteNewValue);
                return;
            }

            throw new NotImplementedException(
                $"{nameof(RemoteFieldInfo)}.{nameof(SetValue)} only supports {nameof(RemoteObject)} or {nameof(DynamicRemoteObject)} targets.");
        }

        public override FieldAttributes Attributes { get; }
        public override RuntimeFieldHandle FieldHandle { get; }
    }

    public class RemotePropertyInfo : PropertyInfo
    {
        private RemoteApp App => (DeclaringType as RemoteType)?.App;
        public override PropertyAttributes Attributes => throw new NotImplementedException();

        public override bool CanRead => GetMethod != null;
        public override bool CanWrite => SetMethod != null;

        public override Type PropertyType { get; }

        public override Type DeclaringType { get; }

        public override string Name { get; }

        public override Type ReflectedType => throw new NotImplementedException();

        public RemoteMethodInfo GetMethod { get; set; }
        public RemoteMethodInfo SetMethod { get; set; }

        public RemotePropertyInfo(Type declaringType, Type propType, string name)
        {
            PropertyType = propType;
            DeclaringType = declaringType;
            Name = name;
        }

        public override MethodInfo[] GetAccessors(bool nonPublic)
        {
            throw new NotImplementedException();
        }

        public override object[] GetCustomAttributes(bool inherit)
        {
            throw new NotImplementedException();
        }

        public override object[] GetCustomAttributes(Type attributeType, bool inherit)
        {
            throw new NotImplementedException();
        }

        public override MethodInfo GetGetMethod(bool nonPublic) => this.GetMethod;
        public override MethodInfo GetSetMethod(bool nonPublic) => this.SetMethod;

        public override ParameterInfo[] GetIndexParameters()
        {
            throw new NotImplementedException();
        }


        public override object GetValue(object obj, BindingFlags invokeAttr, Binder binder, object[] index, CultureInfo culture)
        {
            RemoteMethodInfo getMethod = GetGetMethod() as RemoteMethodInfo;
            if (getMethod != null)
            {
                return getMethod.Invoke(obj, null);
            }
            else
            {
                throw new Exception($"Couldn't retrieve 'get' method of property '{this.Name}'");
            }
        }

        public override bool IsDefined(Type attributeType, bool inherit)
        {
            throw new NotImplementedException();
        }

        public override void SetValue(object obj, object value, BindingFlags invokeAttr, Binder binder, object[] index, CultureInfo culture)
        {
            RemoteMethodInfo setMethod = GetSetMethod() as RemoteMethodInfo;
            if (setMethod != null)
            {
                setMethod.Invoke(obj, null);
            }
            else
            {
                throw new Exception($"Couldn't retrieve 'set' method of property '{this.Name}'");
            }
        }
    }
}