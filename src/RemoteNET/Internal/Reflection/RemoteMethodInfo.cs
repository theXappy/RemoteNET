using System;
using System.Globalization;
using System.Linq;
using System.Reflection;

namespace RemoteNET.Internal.Reflection
{
    [System.Diagnostics.DebuggerDisplay("??? {Name}( ... )")]
    public class RemoteMethodInfo : MethodInfo
    {
        private Lazy<Type> _retType;

        public override ICustomAttributeProvider ReturnTypeCustomAttributes => throw new NotImplementedException();
        public override string Name { get; }
        public override Type DeclaringType { get; }
        public override Type ReturnType => _retType.Value;
        public override Type ReflectedType => throw new NotImplementedException();
        public override RuntimeMethodHandle MethodHandle => throw new NotImplementedException();
        public override MethodAttributes Attributes => throw new NotImplementedException();

        public Type[] AssignedGenericArgs { get; }
        private readonly ParameterInfo[] _paramInfos;

        private RemoteApp App => (DeclaringType as RemoteType)?.App;

        public RemoteMethodInfo(RemoteType declaringType, MethodInfo mi) :
            this(declaringType,
                new Lazy<Type>(()=>mi.ReturnType),
                mi.Name,
                mi.GetGenericArguments(),
                mi.GetParameters().Select(pi => new RemoteParameterInfo(pi)).Cast<ParameterInfo>().ToArray())
        {
        }
        public RemoteMethodInfo(Type declaringType, Lazy<Type> returnType, string name, Type[] genericArgs, ParameterInfo[] paramInfos)
        {
            Name = name;
            DeclaringType = declaringType;
            _paramInfos = paramInfos;
            _retType = returnType;

            if(genericArgs == null)
            {
                genericArgs = new Type[0];
            }
            AssignedGenericArgs = genericArgs;
        }
        public RemoteMethodInfo(Type declaringType, Type returnType, string name, Type[] genericArgs, ParameterInfo[] paramInfos) :
            this(declaringType, new Lazy<Type>(() => returnType), name, genericArgs, paramInfos)
        { 
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
            return RemoteFunctionsInvokeHelper.Invoke(this.App, DeclaringType, Name, obj, AssignedGenericArgs, parameters);
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
            try
            {
                string args = string.Join(", ", _paramInfos.Select(pi => pi.ParameterType.FullName));
                return $"{this.ReturnType.FullName} {this.Name}({args})";
            }
            catch (Exception ex)
            {
                throw;
            }
        }
    }
}