using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using RemoteNET.Common;

namespace RemoteNET.Internal.Reflection.DotNet
{
    public class RemoteType : RemoteTypeBase
    {
        private readonly List<RemoteConstructorInfo> _ctors = new List<RemoteConstructorInfo>();
        private readonly List<RemoteMethodInfo> _methods = new List<RemoteMethodInfo>();
        private readonly List<RemoteFieldInfo> _fields = new List<RemoteFieldInfo>();
        private readonly List<RemotePropertyInfo> _properties = new List<RemotePropertyInfo>();
        private readonly List<RemoteEventInfo> _events = new List<RemoteEventInfo>();
        private readonly bool _isArray;
        private readonly bool _isGenericParameter;

        public ManagedRemoteApp App { get; set; }

        public override bool IsGenericParameter => _isGenericParameter;

        private Lazy<Type> _parent;
        public override Type BaseType => _parent?.Value;

        public RemoteType(ManagedRemoteApp app, Type localType) : this(app, localType.FullName, localType.Assembly.GetName().Name, localType.IsArray, localType.IsGenericParameter)
        {
            if (localType is RemoteType)
            {
                throw new ArgumentException("This constructor of RemoteType is designed to copy a LOCAL Type object. A RemoteType object was provided instead.");
            }

            // TODO: This ctor is experimentatl because it makes a LOT of assumptions.
            // Most notably the RemoteXXXInfo objects freely use mi's,ci's,pi's (etc) "ReturnType","FieldType","PropertyType"
            // not checking if they are actually RemoteTypes themselves...

            BindingFlags allRecursive = ~(BindingFlags.DeclaredOnly);
            foreach (MethodInfo mi in localType.GetMethods(allRecursive))
                AddMethod(new RemoteMethodInfo(this, mi));
            foreach (ConstructorInfo ci in localType.GetConstructors(allRecursive))
                AddConstructor(new RemoteConstructorInfo(this, ci));
            foreach (PropertyInfo pi in localType.GetProperties(allRecursive))
            {
                RemotePropertyInfo remotePropInfo = new RemotePropertyInfo(this, pi);
                remotePropInfo.RemoteGetMethod = _methods.FirstOrDefault(m => m.Name == "get_" + pi.Name);
                remotePropInfo.RemoteSetMethod = _methods.FirstOrDefault(m => m.Name == "set_" + pi.Name);
                AddProperty(remotePropInfo);
            }
            foreach (FieldInfo fi in localType.GetFields(allRecursive))
                AddField(new RemoteFieldInfo(this, fi));
            foreach (EventInfo ei in localType.GetEvents(allRecursive))
                AddEvent(new RemoteEventInfo(this, ei));
        }

        public RemoteType(ManagedRemoteApp app, string fullName, string assemblyName, bool isArray, bool isGenericParameter = false)
        {
            App = app;
            FullName = fullName;
            _isGenericParameter = isGenericParameter;
            _isArray = isArray;
            Assembly = new RemoteAssemblyDummy(assemblyName);

            // Derieving name from full name
            Name = fullName.Substring(fullName.LastIndexOf('.') + 1);
            if (fullName.Contains("`"))
            {
                // Generic. Need to cut differently
                string outterTypeFullName = fullName.Substring(0, fullName.IndexOf('`'));
                Name = outterTypeFullName.Substring(outterTypeFullName.LastIndexOf('.') + 1);
            }
        }

        public void AddConstructor(RemoteConstructorInfo rci)
        {
            _ctors.Add(rci);
        }

        public void AddMethod(RemoteMethodInfo rmi)
        {
            _methods.Add(rmi);
        }

        public void AddField(RemoteFieldInfo fieldInfo)
        {
            _fields.Add(fieldInfo);
        }
        public void AddProperty(RemotePropertyInfo fieldInfo)
        {
            _properties.Add(fieldInfo);
        }
        public void AddEvent(RemoteEventInfo eventInfo)
        {
            _events.Add(eventInfo);
        }

        public override object[] GetCustomAttributes(bool inherit)
        {
            throw new NotImplementedException();
        }

        public override bool IsDefined(Type attributeType, bool inherit)
        {
            throw new NotImplementedException();
        }

        public override ConstructorInfo[] GetConstructors(BindingFlags bindingAttr) => _ctors.Cast<ConstructorInfo>().ToArray();

        public override Type GetInterface(string name, bool ignoreCase)
        {
            throw new NotImplementedException();
        }

        public override Type[] GetInterfaces()
        {
            throw new NotImplementedException();
        }

        public override EventInfo GetEvent(string name, BindingFlags bindingAttr)
        {
            return GetEvents().Single(ei => ei.Name == name);
        }

        public override EventInfo[] GetEvents(BindingFlags bindingAttr)
        {
            return _events.ToArray();
        }

        public override Type[] GetNestedTypes(BindingFlags bindingAttr)
        {
            throw new NotImplementedException();
        }

        public override Type GetNestedType(string name, BindingFlags bindingAttr)
        {
            throw new NotImplementedException();
        }

        public override Type GetElementType()
        {
            throw new NotImplementedException();
        }

        protected override bool HasElementTypeImpl()
        {
            throw new NotImplementedException();
        }

        protected override PropertyInfo GetPropertyImpl(string name, BindingFlags bindingAttr, Binder binder, Type returnType, Type[] types,
            ParameterModifier[] modifiers)
        {
            return GetProperties().Single(prop => prop.Name == name);
        }

        public override PropertyInfo[] GetProperties(BindingFlags bindingAttr)
        {
            return _properties.ToArray();
        }

        protected override MethodInfo GetMethodImpl(string name, BindingFlags bindingAttr, Binder binder, CallingConventions callConvention,
            Type[] types, ParameterModifier[] modifiers)
        {
            var methodGroup = GetMethods().Where(method =>
                method.Name == name);
            if (types == null)
            {
                // Parameters unknown from caller. Hope we have only one method to return.
                return methodGroup.Single();
            }

            bool overloadsComparer(MethodInfo method)
            {
                var parameters = method.GetParameters();
                // Compare Full Names mainly because the RemoteMethodInfo contains RemoteParameterInfos and we might be 
                // comparing with local parameters (like System.String)
                bool matchingExpectingTypes = parameters
                    .Select(arg => arg.ParameterType.FullName)
                    .SequenceEqual(types.Select(type => type.FullName));
                return matchingExpectingTypes;
            }

            // Need to filer also by types
            return methodGroup.Single(overloadsComparer);
        }


        public override MethodInfo[] GetMethods(BindingFlags bindingAttr)
        {
            return _methods.ToArray();
        }

        public override FieldInfo GetField(string name, BindingFlags bindingAttr)
        {
            return GetFields().Single(field => field.Name == name);
        }

        public override FieldInfo[] GetFields(BindingFlags bindingAttr)
        {
            return _fields.ToArray();
        }

        private IEnumerable<MemberInfo> GetMembersInner(BindingFlags bf)
        {
            foreach (var ctor in GetConstructors(bf))
            {
                yield return ctor;
            }
            foreach (var field in GetFields(bf))
            {
                yield return field;
            }
            foreach (var prop in GetProperties(bf))
            {
                yield return prop;
            }
            foreach (var eventt in GetEvents(bf))
            {
                yield return eventt;
            }
            foreach (var method in GetMethods(bf))
            {
                yield return method;
            }
        }

        internal void SetParent(Lazy<Type> parent)
        {
            _parent = parent;
        }

        public override MemberInfo[] GetMembers(BindingFlags bindingAttr) => GetMembersInner(bindingAttr).ToArray();

        protected override TypeAttributes GetAttributeFlagsImpl()
        {
            throw new NotImplementedException();
        }

        protected override bool IsArrayImpl()
        {
            return _isArray;
        }

        protected override bool IsByRefImpl()
        {
            throw new NotImplementedException();
        }

        protected override bool IsPointerImpl()
        {
            throw new NotImplementedException();
        }

        protected override bool IsPrimitiveImpl()
        {
            throw new NotImplementedException();
        }

        protected override bool IsCOMObjectImpl()
        {
            throw new NotImplementedException();
        }

        public override object InvokeMember(string name, BindingFlags invokeAttr, Binder binder, object target, object[] args,
            ParameterModifier[] modifiers, CultureInfo culture, string[] namedParameters)
        {
            throw new NotImplementedException();
        }

        public override Type UnderlyingSystemType { get; }

        protected override ConstructorInfo GetConstructorImpl(BindingFlags bindingAttr, Binder binder, CallingConventions callConvention,
            Type[] types, ParameterModifier[] modifiers)
        {
            throw new NotImplementedException();
        }

        public override string Name { get; }
        public override Guid GUID { get; }
        public override Module Module { get; }
        public override Assembly Assembly { get; }
        public override string FullName { get; }
        public override string Namespace { get; }
        public override string AssemblyQualifiedName { get; }

        public override object[] GetCustomAttributes(Type attributeType, bool inherit)
        {
            throw new NotImplementedException();
        }

        public override string ToString() => FullName;
    }
}