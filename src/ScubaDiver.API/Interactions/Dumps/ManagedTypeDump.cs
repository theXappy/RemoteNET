using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reflection;

namespace ScubaDiver.API.Interactions.Dumps
{
    [DebuggerDisplay("ManagedTypeDump of {" + nameof(Type) + "} (Assembly: {" + nameof(Assembly) + "})")]
    public class ManagedTypeDump
    {
        public class TypeMethod
        {
            public class MethodParameter
            {
                public bool IsGenericType { get; set; }
                public bool IsGenericParameter { get; set; }
                public string FullTypeName { get; set; }
                public string TypeName { get; set; }
                public string Name { get; set; }
                public string Assembly { get; set; }

                public MethodParameter()
                {

                }

                public MethodParameter(ParameterInfo pi)
                {
                    IsGenericType = pi.ParameterType.IsGenericType;
                    IsGenericParameter = pi.ParameterType.IsGenericParameter || pi.ParameterType.ContainsGenericParameters;
                    Name = pi.Name;
                    // For generic type parameters we need the 'Name' property - it returns something like "T"
                    // For non-generic we want the full name like "System.Text.StringBuilder"
                    TypeName = pi.ParameterType.Name;
                    FullTypeName = pi.ParameterType.FullName;
                    if (IsGenericParameter &&
                        pi.ParameterType.GenericTypeArguments.Any() &&
                        FullTypeName.Contains('`'))
                    {
                        FullTypeName = FullTypeName.Substring(0, FullTypeName.IndexOf('`'));
                        FullTypeName += '<';
                        FullTypeName += String.Join(", ", (object[])pi.ParameterType.GenericTypeArguments);
                        FullTypeName += '>';
                    }
                    Assembly = pi.ParameterType.Assembly.GetName().Name;
                }

                public override string ToString()
                {
                    return
                        (string.IsNullOrEmpty(Assembly) ? string.Empty : Assembly + ".") +
                        (string.IsNullOrEmpty(FullTypeName) ? "UNKNOWN_TYPE" : FullTypeName) + " " +
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
            public string ReturnTypeName { get; set; }

            public TypeMethod()
            {
            }

            public TypeMethod(MethodBase methodBase)
            {
                Visibility = methodBase.IsPublic ? "Public" : "Private";
                GenericArgs = new List<string>();
                if (methodBase.ContainsGenericParameters && methodBase is not ConstructorInfo)
                {
                    try
                    {
                        GenericArgs = methodBase.GetGenericArguments().Select(arg => arg.Name).ToList();
                    }
                    catch (Exception)
                    {
                    }
                }

                Name = methodBase.Name;
                Parameters = methodBase.GetParameters().Select(paramInfo => new MethodParameter(paramInfo)).ToList();
                if (methodBase is MethodInfo methodInfo)
                {
                    ReturnTypeName = methodInfo.ReturnType.Name;
                    ReturnTypeFullName = methodInfo.ReturnType.FullName;
                    if (ReturnTypeFullName == null)
                    {
                        string baseType = methodInfo.ReturnType.Name;
                        if (baseType.Contains('`'))
                            baseType = baseType.Substring(0, baseType.IndexOf('`'));
                        ReturnTypeFullName ??= baseType + "<" +
                                               String.Join(", ", (object[])methodInfo.ReturnType.GenericTypeArguments) +
                                               ">";
                    }

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
                           param1.FullTypeName == param2.FullTypeName;
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