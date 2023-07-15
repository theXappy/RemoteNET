using System;
using System.Globalization;
using System.Reflection;

namespace RemoteNET.Internal.Reflection.DotNet
{
    public class RemotePropertyInfo : PropertyInfo
    {
        private Lazy<Type> _propType;

        private ManagedRemoteApp App => (DeclaringType as RemoteType)?.App;
        public override PropertyAttributes Attributes => throw new NotImplementedException();

        public override bool CanRead => GetMethod != null;
        public override bool CanWrite => SetMethod != null;

        public override Type PropertyType => _propType.Value;

        public override Type DeclaringType { get; }

        public override string Name { get; }

        public override Type ReflectedType => throw new NotImplementedException();

        public RemoteMethodInfo RemoteGetMethod { get; set; }
        public RemoteMethodInfo RemoteSetMethod { get; set; }

        public override MethodInfo GetMethod => RemoteGetMethod;
        public override MethodInfo SetMethod => RemoteSetMethod;

        public RemotePropertyInfo(Type declaringType, Lazy<Type> propType, string name)
        {
            _propType = propType;
            DeclaringType = declaringType;
            Name = name;
        }
        public RemotePropertyInfo(Type declaringType, Type propType, string name) :
            this(declaringType, new Lazy<Type>(() => propType), name)
        {
        }

        public RemotePropertyInfo(RemoteType declaringType, PropertyInfo pi) : this(declaringType, new Lazy<Type>(() => pi.PropertyType), pi.Name)
        {
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

        public override MethodInfo GetGetMethod(bool nonPublic) => GetMethod;
        public override MethodInfo GetSetMethod(bool nonPublic) => SetMethod;

        public override ParameterInfo[] GetIndexParameters()
        {
            throw new NotImplementedException();
        }


        public override object GetValue(object obj, BindingFlags invokeAttr, Binder binder, object[] index, CultureInfo culture)
        {
            RemoteMethodInfo getMethod = GetGetMethod() as RemoteMethodInfo;
            if (getMethod != null)
            {
                return getMethod.Invoke(obj, new object[0]);
            }
            else
            {
                throw new Exception($"Couldn't retrieve 'get' method of property '{Name}'");
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
                setMethod.Invoke(obj, new object[1] { value });
            }
            else
            {
                throw new Exception($"Couldn't retrieve 'set' method of property '{Name}'");
            }
        }

        public override string ToString()
        {
            return $"{PropertyType.FullName ?? PropertyType.Name} {Name}";
        }
    }
}