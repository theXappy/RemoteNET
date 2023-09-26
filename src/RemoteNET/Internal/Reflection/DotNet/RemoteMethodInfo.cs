using System;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Text;
using RemoteNET.Common;

namespace RemoteNET.Internal.Reflection.DotNet
{
    public class RemoteMethodInfo : RemoteMethodInfoBase
    {
        private LazyRemoteTypeResolver _retType;

        public override ICustomAttributeProvider ReturnTypeCustomAttributes => throw new NotImplementedException();
        public override string Name { get; }
        public override Type DeclaringType { get; }
        public override Type ReturnType => _retType.Value;
        public override Type ReflectedType => throw new NotImplementedException();
        public override RuntimeMethodHandle MethodHandle => throw new NotImplementedException();
        public override MethodAttributes Attributes => throw new NotImplementedException();

        public override bool IsGenericMethod => AssignedGenericArgs.Length > 0;
        public override bool IsGenericMethodDefinition => AssignedGenericArgs.Length > 0 && AssignedGenericArgs.All(t => t is DummyGenericType);
        public override bool ContainsGenericParameters => AssignedGenericArgs.Length > 0 && AssignedGenericArgs.All(t => t is DummyGenericType);
        public override Type[] GetGenericArguments() => AssignedGenericArgs;

        public Type[] AssignedGenericArgs { get; }
        private readonly ParameterInfo[] _paramInfos;

        private ManagedRemoteApp App => (DeclaringType as RemoteType)?.App;

        public RemoteMethodInfo(RemoteType declaringType, MethodInfo mi) :
            this(declaringType,
                new LazyRemoteTypeResolver(mi.ReturnType),
                mi.Name,
                mi.GetGenericArguments(),
                mi.GetParameters().Select(pi => new RemoteParameterInfo(pi)).Cast<ParameterInfo>().ToArray())
        {
        }
        public RemoteMethodInfo(Type declaringType, LazyRemoteTypeResolver returnType, string name, Type[] genericArgs, ParameterInfo[] paramInfos)
        {
            Name = name;
            DeclaringType = declaringType;
            _paramInfos = paramInfos;
            _retType = returnType;

            genericArgs ??= Type.EmptyTypes;
            AssignedGenericArgs = genericArgs;
        }

        public RemoteMethodInfo(Type declaringType, Type returnType, string name, Type[] genericArgs, ParameterInfo[] paramInfos) :
            this(declaringType, new LazyRemoteTypeResolver(returnType), name, genericArgs, paramInfos)
        {
        }

        public override MethodInfo MakeGenericMethod(params Type[] typeArguments)
        {
            return new RemoteMethodInfo(DeclaringType, ReturnType, Name, typeArguments, _paramInfos);
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
            return ManagedRemoteFunctionsInvokeHelper.Invoke(App, DeclaringType, Name, obj, AssignedGenericArgs, parameters);
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
            StringBuilder sb = new StringBuilder();
            sb.Append($"{_retType.TypeFullName ?? _retType.TypeName} ");
            sb.Append($"{Name}");
            if (ContainsGenericParameters)
            {
                string generics = string.Join(", ", AssignedGenericArgs.Select(argType => argType.ToString()));
                sb.Append($"<{generics}>");
            }
            string args = string.Join(", ", _paramInfos.Select(pi => pi.ToString()));
            sb.Append($"({args})");
            return sb.ToString();
        }
    }
}