using System;
using System.Globalization;
using System.Reflection;

namespace RemoteNET.Internal.Reflection
{
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

        public override string ToString()
        {
            return $"{PropertyType.FullName} {Name}";
        }
    }
}