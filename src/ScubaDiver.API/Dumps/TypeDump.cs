using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace ScubaDiver.API.Dumps
{
    public class TypeDump
    {
        public class TypeMethod
        {
            public class MethodParameter
            {
                public string Type { get; set; }
                public string Name { get; set; }
                public string Assembly { get; set; }

                public MethodParameter()
                {
                    
                }

                public MethodParameter(ParameterInfo pi)
                {
                    Name = pi.Name;
                    Type = pi.ParameterType.FullName;
                    Assembly = pi.ParameterType.Assembly.GetName().Name;
                }

                public override string ToString()
                {
                    return 
                        (string.IsNullOrEmpty(Assembly) ? string.Empty : (Assembly+".")) +
                        (string.IsNullOrEmpty(Type) ? "UNKNOWN_TYPE" : Type) + " " +
                           (string.IsNullOrEmpty(Name) ? "MISSING_NAME" : Name);

                }
            }

            public string Visibility { get; set; }
            public string Name { get; set; }
            public string ReturnTypeFullName { get; set; }
            public List<MethodParameter> Parameters { get; set; }
            public string ReturnTypeAssembly { get; set; }
            public bool ContainsGenericParameters { get; set; }

            public TypeMethod()
            {
            }

            public TypeMethod(MethodBase methodBase)
            {
                Visibility = methodBase.IsPublic ? "Public" : "Private";
                ContainsGenericParameters = methodBase.ContainsGenericParameters;
                Name = methodBase.Name;
                Parameters = methodBase.GetParameters().Select(paramInfo => new MethodParameter(paramInfo)).ToList();
                if(methodBase is MethodInfo methodInfo)
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
                if(Parameters.Count != other.Parameters.Count)
                    return false;
                var paramMatches = Parameters.Zip(other.Parameters, (param1, param2) =>
                {
                    return param1.Name == param2.Name &&
                           param1.Type == param2.Type;
                });
                return paramMatches.All(match => match == true);
            }

            public override string ToString()
            {

                return $"{this.ReturnTypeFullName} {this.Name}({string.Join(",", Parameters)})";
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
            public TypeEvent()
            {
            }
            public TypeEvent(EventInfo ei)
            {
                this.Name = ei.Name;
                this.TypeFullName = ei.EventHandlerType.FullName;
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

        public TypeDump? ParentDump { get; set; }

        public List<TypeMethod> Methods { get; set; }
        public List<TypeMethod> Constructors { get; set; }
        public List<TypeField> Fields { get; set; }
        public List<TypeEvent> Events { get; set; }
        public List<TypeProperty> Properties { get; set; }
    }
}