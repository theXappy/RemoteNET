using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;

namespace RemoteNET.Internal.Reflection
{
    public class RemoteUnmanagedType : Type
    {
        public override Module Module { get; }
        private string _namespace;
        private string _name;
        private string _fullName;
        public override string Namespace => _namespace;
        public override string Name => _name;
        public override string FullName => _fullName;

        private Dictionary<string, MethodInfo> _methods = new Dictionary<string, MethodInfo>();
        private List<ConstructorInfo> _ctors = new List<ConstructorInfo>();

        public RemoteUnmanagedType(string fullTypeName)
        {
            _fullName = fullTypeName;
            _name = fullTypeName;
            _namespace = string.Empty;

            int scopeResolutionIndex = fullTypeName.IndexOf("::");
            if (scopeResolutionIndex != -1)
            {
                _namespace = fullTypeName.Substring(0, scopeResolutionIndex);
                _name = fullTypeName.Substring(scopeResolutionIndex + 2); // Add 2 to skip the "::" separator
            }
        }

        public void AddMethod(MethodInfo mi) => _methods.Add(mi.Name, mi);

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


        protected override TypeAttributes GetAttributeFlagsImpl()
        {
            throw new NotImplementedException();
        }

        protected override ConstructorInfo GetConstructorImpl(BindingFlags bindingAttr, Binder binder, CallingConventions callConvention,
            Type[] types, ParameterModifier[] modifiers)
        {
            throw new NotImplementedException();
        }

        public override ConstructorInfo[] GetConstructors(BindingFlags bindingAttr)
        {
            return _ctors.ToArray();
        }

        public override Type GetElementType()
        {
            throw new NotImplementedException();
        }

        public override EventInfo GetEvent(string name, BindingFlags bindingAttr)
        {
            // No such a thing
            return null;
        }

        public override EventInfo[] GetEvents(BindingFlags bindingAttr)
        {
            // No such a thing
            return Array.Empty<EventInfo>();
        }

        public override FieldInfo GetField(string name, BindingFlags bindingAttr)
        {
            // Fields Not Supported
            return null;
        }

        public override FieldInfo[] GetFields(BindingFlags bindingAttr)
        {
            // Fields Not Supported
            return Array.Empty<FieldInfo>();
        }

        public override MemberInfo[] GetMembers(BindingFlags bindingAttr) => GetMembersInner(bindingAttr).ToArray();
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

        protected override MethodInfo GetMethodImpl(string name, BindingFlags bindingAttr, Binder binder, CallingConventions callConvention,
            Type[] types, ParameterModifier[] modifiers)
        {
            throw new NotImplementedException();
        }

        public override MethodInfo[] GetMethods(BindingFlags bindingAttr)
        {
            return _methods.Values.ToArray();
        }

        public override PropertyInfo[] GetProperties(BindingFlags bindingAttr)
        {
            // No such a thing
            return Array.Empty<PropertyInfo>();
        }

        public override object InvokeMember(string name, BindingFlags invokeAttr, Binder binder, object target, object[] args,
            ParameterModifier[] modifiers, CultureInfo culture, string[] namedParameters)
        {
            throw new NotImplementedException();
        }

        public override Type UnderlyingSystemType { get; }

        protected override bool IsArrayImpl()
        {
            // No such a thing
            return false;
        }

        protected override bool IsByRefImpl()
        {
            // No such a thing
            return false;
        }

        protected override bool IsCOMObjectImpl()
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

        public override Assembly Assembly { get; }
        public override string AssemblyQualifiedName { get; }
        public override Type BaseType { get; }
        public override Guid GUID { get; }

        protected override PropertyInfo GetPropertyImpl(string name, BindingFlags bindingAttr, Binder binder, Type returnType, Type[] types,
            ParameterModifier[] modifiers)
        {
            throw new NotImplementedException();
        }

        protected override bool HasElementTypeImpl()
        {
            throw new NotImplementedException();
        }

        public override Type GetNestedType(string name, BindingFlags bindingAttr)
        {
            throw new NotImplementedException();
        }

        public override Type[] GetNestedTypes(BindingFlags bindingAttr)
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
    }
}