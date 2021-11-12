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

            public TypeMethod(MethodInfo mi)
            {
                Visibility = mi.IsPublic ? "Public" : "Private";
                ContainsGenericParameters = mi.ContainsGenericParameters;
                Name = mi.Name;
                ReturnTypeFullName = mi.ReturnType.FullName;
                ReturnTypeAssembly = mi.ReturnType.Assembly.GetName().Name;
                Parameters = mi.GetParameters().Select(pi => new MethodParameter(pi)).ToList();
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

            public TypeField()
            {
            }

            public TypeField(FieldInfo fi)
            {
                Visibility = fi.IsPublic ? "Public" : "Private";
                Name = fi.Name;
                TypeFullName = fi.FieldType.FullName;
            }

        }
        public class TypeProperty
        {
            public string GetVisibility { get; set; }
            public string SetVisibility { get; set; }
            public string Name { get; set; }
            public string TypeFullName { get; set; }

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
            }
        }

        public string Type { get; set; }
        public string Assembly { get; set; }

        public TypeDump? ParentDump { get; set; }

        public List<TypeMethod> Methods { get; set; }
        public List<TypeField> Fields { get; set; }
        public List<TypeProperty> Properties { get; set; }
    }
}