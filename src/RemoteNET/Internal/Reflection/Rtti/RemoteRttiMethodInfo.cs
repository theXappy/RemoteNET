using System;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Reflection;
using RemoteNET.Common;
using RemoteNET.RttiReflection;

namespace RemoteNET.Internal.Reflection
{

    [DebuggerDisplay("Remote RTTI Method: {UndecoratedSignature}")]
    public class RemoteRttiMethodInfo : RemoteMethodInfoBase, IRttiMethodBase
    {
        protected LazyRemoteTypeResolver _lazyRetTypeImpl;
        public LazyRemoteTypeResolver LazyRetType => _lazyRetTypeImpl;
        protected ParameterInfo[] _lazyParamInfosImpl;
        public ParameterInfo[] LazyParamInfos => _lazyParamInfosImpl;

        public override ICustomAttributeProvider ReturnTypeCustomAttributes => throw new NotImplementedException();
        public override string Name { get; }
        public override Type DeclaringType { get; }
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

        public RemoteRttiMethodInfo(RemoteRttiType declaringType, MethodInfo mi) :
            this(declaringType,
                new LazyRemoteTypeResolver(mi.ReturnType),
                mi.Name,
                (mi as RemoteRttiMethodInfo)?.MangledName ?? mi.Name,
                mi.GetParameters().Select(pi => new RemoteParameterInfo(pi)).Cast<ParameterInfo>().ToArray())
        {
        }
        public RemoteRttiMethodInfo(Type declaringType, LazyRemoteTypeResolver returnType, string name, string mangledName, ParameterInfo[] lazyParamInfos)
        {
            Name = name;
            MangledName = mangledName;
            DeclaringType = declaringType;
            _lazyParamInfosImpl = lazyParamInfos;
            _lazyRetTypeImpl = returnType;

            AssignedGenericArgs = Type.EmptyTypes;
        }

        public RemoteRttiMethodInfo(Type declaringType, Type returnType, string name, ParameterInfo[] paramInfos) :
            this(declaringType, new LazyRemoteTypeResolver(returnType), name, name, paramInfos)
        {
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
            // Skipping 'this'
            return LazyParamInfos.Skip(1).ToArray();
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