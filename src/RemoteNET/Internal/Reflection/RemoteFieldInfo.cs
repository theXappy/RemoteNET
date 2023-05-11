using System;
using System.Globalization;
using System.Reflection;
using ScubaDiver.API;
using ScubaDiver.API.Utils;

namespace RemoteNET.Internal.Reflection
{
    public class RemoteFieldInfo : FieldInfo
    {
        private ManagedRemoteApp App => (DeclaringType as RemoteType)?.App;
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

        private Lazy<Type> _fieldType;
        public override Type FieldType => _fieldType.Value;
        public override Type DeclaringType { get; }
        public override string Name { get; }

        public RemoteFieldInfo(Type declaringType, Lazy<Type> fieldType, string name)
        {
            _fieldType = fieldType;
            DeclaringType = declaringType;
            Name = name;
        }

        public RemoteFieldInfo(Type declaringType, Type fieldType, string name) : this(declaringType, new Lazy<Type>(() => fieldType), name)
        {
        }

        public RemoteFieldInfo(RemoteType declaringType, FieldInfo fi) : this(declaringType, new Lazy<Type>(()=> fi.FieldType), fi.Name)
        {
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
                                                        $"Declaring Type ({this.DeclaringType}) does not have a ManagedRemoteApp associated. " +
                                                        $"The type was either mis-constructed or it's not a {nameof(RemoteType)} object");
                }

                oora = this.App.ManagedCommunicator.GetField(0, DeclaringType.FullName, this.Name).ReturnedObjectOrAddress;
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
                    // I only support managed here because I don't think I'll implement "Field Infos" for unmanaged
                    // objects any time soon.
                    var remoteObject = this.App.GetRemoteObject(oora.RemoteAddress, oora.Type);
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
                                                        $"Declaring Type ({this.DeclaringType}) does not have a ManagedRemoteApp associated. " +
                                                        $"The type was either mis-constructed or it's not a {nameof(RemoteType)} object");
                }

                this.App.ManagedCommunicator.SetField(0, DeclaringType.FullName, this.Name, remoteNewValue);
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

        public override string ToString()
        {
            return $"{FieldType.FullName ?? FieldType.Name} {Name}";
        }
    }
}