using System;
using System.Globalization;
using System.Reflection;

namespace RemoteNET.Internal.Reflection
{
    public class RemoteUnmanagedMethodInfo : MethodInfo
    {
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

        public override string Name { get; }
        public override Type DeclaringType { get; }
        public override Type ReflectedType { get; }

        public RemoteUnmanagedMethodInfo(string name, Type declaringType)
        {
            Name = name;
            int scopeResolutionIndex = name.LastIndexOf("::");
            if (scopeResolutionIndex != -1)
            {
                Name = name.Substring(scopeResolutionIndex + 2); // Add 2 to skip the "::" separator
            }

            DeclaringType = declaringType;
            ReflectedType = declaringType;
        }

        public override MethodImplAttributes GetMethodImplementationFlags()
        {
            throw new NotImplementedException();
        }

        public override ParameterInfo[] GetParameters()
        {
            throw new NotImplementedException();
        }

        public override object Invoke(object obj, BindingFlags invokeAttr, Binder binder, object[] parameters, CultureInfo culture)
        {
            throw new NotImplementedException();
        }

        public override MethodAttributes Attributes { get; }
        public override RuntimeMethodHandle MethodHandle { get; }
        public override MethodInfo GetBaseDefinition()
        {
            throw new NotImplementedException();
        }

        public override ICustomAttributeProvider ReturnTypeCustomAttributes { get; }

        public override string ToString()
        {
            try
            {
                return $"Unk {Name}(...)";
            }
            catch (Exception)
            {
                throw;
            }
        }
    }
}