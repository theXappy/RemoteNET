using System;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Reflection;
using RemoteNET.Common;
using RemoteNET.RttiReflection;

namespace RemoteNET.Internal.Reflection
{

    [DebuggerDisplay("Remote RTTI Method: {LazyRetType.TypeFullName} {Name}(...)")]
    public class RemoteRttiMethodInfo : RemoteMethodInfoBase, IRttiMethodBase
    {
        protected LazyRemoteTypeResolver _lazyRetTypeImpl;
        public LazyRemoteTypeResolver LazyRetType => _lazyRetTypeImpl;
        protected LazyRemoteParameterResolver[] _lazyParamInfosImpl;
        public LazyRemoteParameterResolver[] LazyParamInfos => _lazyParamInfosImpl;

        public override ICustomAttributeProvider ReturnTypeCustomAttributes => throw new NotImplementedException();
        public override string Name { get; }

        protected LazyRemoteTypeResolver _lazyDeclaringType;
        public LazyRemoteTypeResolver LazyDeclaringType => _lazyDeclaringType;
        public override Type DeclaringType => LazyDeclaringType.Value;
        public override Type ReturnType => LazyRetType.Value;
        public override Type ReflectedType => throw new NotImplementedException();
        public override RuntimeMethodHandle MethodHandle => throw new NotImplementedException();
        public override MethodAttributes Attributes => throw new NotImplementedException();

        public override bool IsGenericMethod => AssignedGenericArgs.Length > 0;
        public override bool IsGenericMethodDefinition => AssignedGenericArgs.Length > 0 && AssignedGenericArgs.All(t => t is DummyGenericType);
        public override bool ContainsGenericParameters => AssignedGenericArgs.Length > 0 && AssignedGenericArgs.All(t => t is DummyGenericType);
        public override Type[] GetGenericArguments() => AssignedGenericArgs;
        public string MangledName { get; private set; }

        public Type[] AssignedGenericArgs { get; }

        private RemoteApp App => (DeclaringType as RemoteRttiType)?.App;


        public RemoteRttiMethodInfo(
            LazyRemoteTypeResolver declaringType, 
            LazyRemoteTypeResolver returnType, 
            string name, 
            string mangledName, 
            LazyRemoteParameterResolver[] lazyParamInfos)
        {
            Name = name;
            MangledName = mangledName;
            _lazyDeclaringType = declaringType;
            _lazyParamInfosImpl = lazyParamInfos;
            _lazyRetTypeImpl = returnType;

            AssignedGenericArgs = Type.EmptyTypes;
        }


        public override MethodInfo MakeGenericMethod(params Type[] typeArguments)
        {
            throw new NotImplementedException();
        }

        public override object[] GetCustomAttributes(bool inherit)
        {
            throw new NotImplementedException();
        }

        public override bool IsDefined(Type attributeType, bool inherit)
        {
            throw new NotImplementedException();
        }

        public override ParameterInfo[] GetParameters()
        {
            // (-1) because we're skipping 'this'
            ParameterInfo[] parameters = new ParameterInfo[_lazyParamInfosImpl.Length - 1];

            for (int i = 1; i < _lazyParamInfosImpl.Length; i++)
            {
                LazyRemoteParameterResolver lazyResolver = _lazyParamInfosImpl[i];
                parameters[i - 1] = new RemoteParameterInfo(lazyResolver.Name, lazyResolver.TypeResolver);
            }

            return parameters;
        }

        public override MethodImplAttributes GetMethodImplementationFlags()
        {
            throw new NotImplementedException();
        }

        public override object Invoke(object obj, BindingFlags invokeAttr, Binder binder, object[] parameters, CultureInfo culture)
        {
            return UnmanagedRemoteFunctionsInvokeHelper.Invoke(this.App as UnmanagedRemoteApp, DeclaringType, Name, obj, parameters);
        }

        public override MethodInfo GetBaseDefinition()
        {
            throw new NotImplementedException();
        }

        public override object[] GetCustomAttributes(Type attributeType, bool inherit)
        {
            throw new NotImplementedException();
        }

        public override string ToString() => MangledName;


    }
}