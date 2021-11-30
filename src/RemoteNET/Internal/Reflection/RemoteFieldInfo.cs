using System;
using System.Globalization;
using System.Reflection;
using ScubaDiver;
using ScubaDiver.API;
using ScubaDiver.API.Extensions;

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
            if (obj == null)
            {
                if (this.App == null)
                {
                    throw new InvalidOperationException($"Trying to get a static field (null target object) " +
                                                        $"on a {nameof(RemoteFieldInfo)} but it's associated " +
                                                        $"Declaring Type ({this.DeclaringType}) does not have a RemoteApp associated. " +
                                                        $"The type was either mis-constructed or it's not a {nameof(RemoteType)} object");
                }

                return this.App.Communicator.GetField(0, DeclaringType.FullName, this.Name).ReturnedObjectOrAddress;
            }

            // obj is NOT null. Make sure it's a RemoteObject.
            if (!(obj is RemoteObject ro))
            {
                throw new NotImplementedException(
                    $"{nameof(RemoteFieldInfo)}.{nameof(GetValue)} only supports {nameof(RemoteObject)} targets at the moment.");
            }

            return ro.GetField(this.Name);
        }

        public override void SetValue(object obj, object value, BindingFlags invokeAttr, Binder binder, CultureInfo culture)
        {
            throw new NotImplementedException();
        }

        public override FieldAttributes Attributes { get; }
        public override RuntimeFieldHandle FieldHandle { get; }
    }
}