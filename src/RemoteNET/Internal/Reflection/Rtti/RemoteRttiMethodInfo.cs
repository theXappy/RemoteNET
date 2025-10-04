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
        private MethodAttributes _attributes;
        public override MethodAttributes Attributes => _attributes;

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
            LazyRemoteParameterResolver[] lazyParamInfos,
            MethodAttributes attributes)
        {
            Name = name;
            MangledName = mangledName;
            _lazyDeclaringType = declaringType;
            _lazyParamInfosImpl = lazyParamInfos;
            _lazyRetTypeImpl = returnType;

            AssignedGenericArgs = Type.EmptyTypes;

            _attributes = attributes;
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
            int start = 0;
            int amount = _lazyParamInfosImpl.Length;
            if (!IsStatic)
            {
                // (-1) because we're skipping 'this' for instance methods
                start = 1;
                amount -= 1;
            }
            ParameterInfo[] parameters = new ParameterInfo[amount];

            int j = 0;
            for (int i = start; i < _lazyParamInfosImpl.Length; i++)
            {
                LazyRemoteParameterResolver lazyResolver = _lazyParamInfosImpl[i];
                parameters[j] = new RemoteParameterInfo(lazyResolver.Name, lazyResolver.TypeResolver);
                j++;
            }

            return parameters;
        }

        public override MethodImplAttributes GetMethodImplementationFlags()
        {
            throw new NotImplementedException();
        }

        public override object Invoke(object obj, BindingFlags invokeAttr, Binder binder, object[] parameters, CultureInfo culture)
        {
            // Using MangledName as it should be more globally unique then Name
            return UnmanagedRemoteFunctionsInvokeHelper.Invoke(this.App as UnmanagedRemoteApp, DeclaringType, MangledName, obj, parameters);
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