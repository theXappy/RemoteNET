﻿using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Text;

namespace RemoteNET.Internal.Reflection
{
    public class RemoteType : Type
    {
        public string RemoteAssemblyName { get; private set; }
        private List<RemoteMethodInfo> _methods = new List<RemoteMethodInfo>();
        private List<RemoteFieldInfo> _fields = new List<RemoteFieldInfo>();
        private bool _isArray;

        public RemoteApp App { get; set; }

        public RemoteType(RemoteApp app, string fullName, string assemblyName, bool isArray)
        {
            App = app;
            this.FullName = fullName;
            this.RemoteAssemblyName = assemblyName;
            this._isArray = isArray;
        }

        public void AddMethod(RemoteMethodInfo rmi)
        {
            _methods.Add(rmi);
        }

        public void AddField(RemoteFieldInfo fieldInfo)
        {
            _fields.Add(fieldInfo);
        }

        public override object[] GetCustomAttributes(bool inherit)
        {
            throw new NotImplementedException();
        }

        public override bool IsDefined(Type attributeType, bool inherit)
        {
            throw new NotImplementedException();
        }

        public override ConstructorInfo[] GetConstructors(BindingFlags bindingAttr)
        {
            throw new NotImplementedException();
        }

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
            throw new NotImplementedException();
        }

        public override EventInfo[] GetEvents(BindingFlags bindingAttr)
        {
            throw new NotImplementedException();
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
            throw new NotImplementedException();
        }

        public override PropertyInfo[] GetProperties(BindingFlags bindingAttr)
        {
            throw new NotImplementedException();
        }


        protected override MethodInfo GetMethodImpl(string name, BindingFlags bindingAttr, Binder binder, CallingConventions callConvention,
            Type[] types, ParameterModifier[] modifiers)
        {
            var methodGroup = _methods.Where(method =>
                method.Name == name);
            if(types == null)
            {
                // Parameters unknown from caller. Hope we have only one method to return.
                return methodGroup.Single();
            }

            // Need to filer also by types
            return methodGroup.Single(method=> method.GetParameters().Select(arg => arg.ParameterType).SequenceEqual(types));
        }

        public override MethodInfo[] GetMethods(BindingFlags bindingAttr)
        {
            return _methods.Cast<MethodInfo>().ToArray();
        }

        public override FieldInfo GetField(string name, BindingFlags bindingAttr)
        {
            return _fields.ToArray().Single(field => field.Name == name);
        }

        public override FieldInfo[] GetFields(BindingFlags bindingAttr)
        {
            return _fields.Cast<FieldInfo>().ToArray();
        }

        public override MemberInfo[] GetMembers(BindingFlags bindingAttr)
        {
            throw new NotImplementedException();
        }

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
        public override Type BaseType { get; }

        public override object[] GetCustomAttributes(Type attributeType, bool inherit)
        {
            throw new NotImplementedException();
        }

        public override string ToString() => FullName;
    }
}