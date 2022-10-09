using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reflection;

namespace ScubaDiver.API.Interactions.Dumps
{
    [DebuggerDisplay("TypeDump of {" + nameof(Type) + "} (Assembly: {" + nameof(Assembly) + "})")]
    public class TypeDump
    {
        public class TypeMethod
        {
            public class MethodParameter
            {
                public bool IsGenericType { get; set; }
                public bool IsGenericParameter { get; set; }
                public string Type { get; set; }
                public string Name { get; set; }
                public string Assembly { get; set; }

                public MethodParameter()
                {

                }

                public MethodParameter(ParameterInfo pi)
                {
                    IsGenericType = pi.ParameterType.IsGenericType;
                    IsGenericParameter = pi.ParameterType.IsGenericParameter;
                    Name = pi.Name;
                    // For generic type parameters we need the 'Name' property - it returns something like "T"
                    // For non-generic we want the full name like "System.Text.StringBuilder"
                    Type = IsGenericParameter ? pi.ParameterType.Name : pi.ParameterType.FullName;
                    Assembly = pi.ParameterType.Assembly.GetName().Name;
                }

                public override string ToString()
                {
                    return
                        (string.IsNullOrEmpty(Assembly) ? string.Empty : Assembly + ".") +
                        (string.IsNullOrEmpty(Type) ? "UNKNOWN_TYPE" : Type) + " " +
                           (string.IsNullOrEmpty(Name) ? "MISSING_NAME" : Name);

                }
            }

            public string Visibility { get; set; }
            public string Name { get; set; }
            public string ReturnTypeFullName { get; set; }
            // This is not a list of the PARAMETERS which are generic -> This is the list of TYPES place holders usually found between
            // the "LESS THEN" and "GEATER THEN" signs so for this methods:
            // void SomeMethod<T,S>(T item, string item2, S item3)
            // You'll get ["T", "S"]
            public List<string> GenericArgs { get; set; }
            public List<MethodParameter> Parameters { get; set; }
            public string ReturnTypeAssembly { get; set; }

            public TypeMethod()
            {
            }

            public TypeMethod(MethodBase methodBase)
            {
                Visibility = methodBase.IsPublic ? "Public" : "Private";
                if (methodBase.ContainsGenericParameters)
                {
                    GenericArgs = methodBase.GetGenericArguments().Select(arg => arg.Name).ToList();
                }
                else
                {
                    GenericArgs = new List<string>();
                }
                Name = methodBase.Name;
                Parameters = methodBase.GetParameters().Select(paramInfo => new MethodParameter(paramInfo)).ToList();
                if (methodBase is MethodInfo methodInfo)
                {
                    ReturnTypeFullName = methodInfo.ReturnType.FullName;
                    ReturnTypeAssembly = methodInfo.ReturnType.Assembly.GetName().Name;
                }
                else
                {
                    ReturnTypeFullName = "System.Void";
                    ReturnTypeAssembly = "mscorlib";

                }
            }

            public bool SignaturesEqual(TypeMethod other)
            {
                if (Name != other.Name)
                    return false;
                if (Parameters.Count != other.Parameters.Count)
                    return false;
                var genericArgsMatches = GenericArgs.Zip(other.GenericArgs, (arg1, arg2) =>
                {
                    return arg1 == arg2;
                });
                var paramMatches = Parameters.Zip(other.Parameters, (param1, param2) =>
                {
                    return param1.Name == param2.Name &&
                           param1.Type == param2.Type;
                });
                return paramMatches.All(match => match == true);
            }

            public override string ToString()
            {

                return $"{ReturnTypeFullName} {Name}({string.Join(",", Parameters)})";
            }
        }
        public class TypeField
        {
            public string Visibility { get; set; }
            public string Name { get; set; }
            public string TypeFullName { get; set; }
            public string Assembly { get; set; }

            public TypeField()
            {
            }

            public TypeField(FieldInfo fi)
            {
                Visibility = fi.IsPublic ? "Public" : "Private";
                Name = fi.Name;
                TypeFullName = fi.FieldType.FullName;
                Assembly = fi.FieldType.Assembly.GetName().Name;
            }
        }
        public class TypeEvent
        {
            public string Name { get; set; }
            public string TypeFullName { get; set; }
            public string Assembly { get; set; }

            public TypeEvent()
            {
            }
            public TypeEvent(EventInfo ei)
            {
                Name = ei.Name;
                TypeFullName = ei.EventHandlerType.FullName;
                Assembly = ei.EventHandlerType.Assembly.GetName().Name;
            }
        }

        public class TypeProperty
        {
            public string GetVisibility { get; set; }
            public string SetVisibility { get; set; }
            public string Name { get; set; }
            public string TypeFullName { get; set; }
            public string Assembly { get; set; }

            public TypeProperty()
            {
            }

            public TypeProperty(PropertyInfo pi)
            {
                if (pi.GetMethod != null)
                {
                    GetVisibility = pi.GetMethod.IsPublic ? "Public" : "Private";
                }
                if (pi.SetMethod != null)
                {
                    SetVisibility = pi.SetMethod.IsPublic ? "Public" : "Private";
                }

                Name = pi.Name;
                TypeFullName = pi.PropertyType.FullName;
                Assembly = pi.PropertyType.Assembly.GetName().Name;
            }
        }

        public string Type { get; set; }
        public string Assembly { get; set; }

        public bool IsArray { get; set; }

        public string ParentFullTypeName { get; set; }
        public string ParentAssembly { get; set; }

        public List<TypeMethod> Methods { get; set; }
        public List<TypeMethod> Constructors { get; set; }
        public List<TypeField> Fields { get; set; }
        public List<TypeEvent> Events { get; set; }
        public List<TypeProperty> Properties { get; set; }
    }
}