using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using RemoteNET.Common;
using RemoteNET.Internal.Reflection;
using RemoteNET.Internal.Reflection.Rtti;

namespace RemoteNET.RttiReflection
{
    public class RemoteRttiType : RemoteTypeBase
    {
        private readonly string _assembly;
        private string _namespace;
        private string _name;
        private string _fullName;
        public override Module Module { get; }
        public override string Namespace => _namespace;
        public override string Name => _name;
        public override string FullName => $"{_assembly}!{_fullName}";

        private List<RemoteRttiMethodTableInfo> _methodTables = new();
        private List<FieldInfo> _fields = new();
        private List<MethodInfo> _methods = new();
        private List<ConstructorInfo> _ctors = new();
        private Lazy<Type> _parent;
        public override Type BaseType => _parent?.Value;
        public RemoteApp App { get; set; }
        public List<string> UnresolvedMembers { get; private set; }

        public RemoteRttiType(RemoteApp app, string fullTypeName, string assemblyName)
        {
            App = app;
            _assembly = assemblyName;
            Assembly = new RemoteAssemblyDummy(assemblyName);
            UnresolvedMembers = new List<string>();


            _fullName = fullTypeName;
            _name = fullTypeName;
            _namespace = string.Empty;

            // Since the generic part might contain subsequent types & we want to ignore their '::'s, we limit
            // our search in the string to the point where the generic part starts.
            // Example: std::vector<std::float>
            int startOfGenericPart = fullTypeName.IndexOf('<');
            if (startOfGenericPart == -1)
                startOfGenericPart = fullTypeName.Length - 1;

            // Namespace is everything but the last part after the last '::'
            // Example: std::somethingelse::realclass
            int scopeResolutionIndex = fullTypeName.LastIndexOf("::", startIndex: startOfGenericPart);
            if (scopeResolutionIndex != -1)
            {
                _namespace = fullTypeName.Substring(0, scopeResolutionIndex);
                _name = fullTypeName.Substring(scopeResolutionIndex + 2); // Add 2 to skip the "::" separator
            }
        }

        internal void SetParent(Lazy<Type> parent)
        {
            _parent = parent;
        }

        public void AddConstructor(ConstructorInfo ci) => _ctors.Add(ci);
        public void AddMethod(MethodInfo mi) => _methods.Add(mi);
        public void AddField(FieldInfo fi) => _fields.Add(fi);
        public void AddMethodTable(RemoteRttiMethodTableInfo mt) => _methodTables.Add(mt);
        public void AddUnresolvedMember(string member) => UnresolvedMembers.Add(member);

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

        public RemoteRttiMethodTableInfo[] GetMethodTables()
        {
            return _methodTables.ToArray();
        }

        public RemoteRttiMethodTableInfo[] GetMethodTables(BindingFlags bindingAttr) => GetMethodTables();

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
            return _fields.ToArray();
        }

        public override MemberInfo[] GetMembers(BindingFlags bindingAttr) => GetMembersInner(bindingAttr).ToArray();
        private IEnumerable<MemberInfo> GetMembersInner(BindingFlags bf)
        {
            foreach (var ctor in GetMethodTables(bf))
            {
                yield return ctor;
            }
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
            int requestTypesCount = types?.Length ?? 0;
            var matches = _methods.Where(m => m.Name == name && m.GetParameters().Length == requestTypesCount).ToArray();
            if (matches.Length == 0)
                return null;
            if (matches.Length == 1)
                return matches[0];
            // If we have multiple matches, we throw
            throw new AmbiguousMatchException($"Multiple methods found with name {name} and {requestTypesCount} parameters");
        }

        public override MethodInfo[] GetMethods(BindingFlags bindingAttr)
        {
            return _methods.ToArray();
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

        public override string ToString() => FullName;

        // I hope this is good enough
        public override int GetHashCode()
        {
            return FullName.GetHashCode();
        }
    }
}