using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Linq;
using System.Reflection;
using RemoteNET.Common;
using RemoteNET.Internal.Reflection;
using RemoteNET.Internal.Reflection.DotNet;
using RemoteNET.Internal.Reflection.Rtti;
using ScubaDiver.API;
using ScubaDiver.API.Interactions.Dumps;

namespace RemoteNET.RttiReflection
{
    public class RttiTypesFactory
    {
        private readonly RttiTypesResolver _resolver;
        private DiverCommunicator _communicator;

        public RttiTypesFactory(RttiTypesResolver resolver, DiverCommunicator communicator)
        {
            _resolver = resolver;
            _communicator = communicator;
        }

        /// <summary>
        /// This collection marks which types the factory is currently creating
        /// it's important since <see cref="Create"/> might recursively call itself and
        /// types might depend on one another (circular references)
        /// </summary>
        private readonly Dictionary<Tuple<string, string>, Type> _onGoingCreations =
            new Dictionary<Tuple<string, string>, Type>();


        public Type ResolveTypeWhileCreating(RemoteApp app, string typeInProgress, string assembly, string type)
        {
            if (type.Length > 500)
            {
                // Too long for any reasonable type
                throw new Exception("Incredibly long type names aren't supported.");
            }


            Type paramType = _resolver.Resolve(assembly, type);
            if (paramType == null)
            {
                // Second: Search types which are on-going creation 
                if (!_onGoingCreations.TryGetValue(
                    new Tuple<string, string>(assembly, type), out paramType) || paramType == null)
                {
                    TypeDump dumpedArgType =
                        _communicator.DumpType(type, assembly);
                    if (dumpedArgType == null)
                    {
                        throw new Exception(
                            $"{nameof(RttiTypesFactory)} tried to dump type {type} when handling method XXX of type" +
                            $"{typeInProgress} but the {nameof(DiverCommunicator)}.{nameof(DiverCommunicator.DumpType)} function failed.");
                    }

                    Type newCreatedType = this.Create(app, dumpedArgType);
                    if (newCreatedType == null)
                    {
                        // remove on-going creation indication
                        throw new Exception(
                            $"{nameof(RttiTypesFactory)} tried to dump type {type} when handling method XXX of type" +
                            $"{typeInProgress} but the inner {nameof(RttiTypesFactory)}.{nameof(RttiTypesFactory.Create)} function failed.");
                    }
                    paramType = newCreatedType;
                }
            }
            return paramType;
        }


        private Type Create(RemoteApp app, string fullTypeName, string assembly)
        {
            Type shortOutput = _resolver.Resolve(assembly, fullTypeName);
            if (shortOutput != null)
            {
                return shortOutput;
            }

            TypeDump parentDump = _communicator.DumpType(fullTypeName, assembly);
            if (parentDump == null)
            {
                throw new Exception(
                    $"{nameof(RttiTypesFactory)} tried to dump type {fullTypeName} " +
                    $"but the {nameof(DiverCommunicator)}.{nameof(DiverCommunicator.DumpType)} function failed.");
            }
            return Create(app, parentDump);
        }

        public Type Create(RemoteApp app, TypeDump typeDump)
        {
            Type shortOutput = _resolver.Resolve(typeDump.Assembly, typeDump.FullTypeName);
            if (shortOutput != null)
            {
                return shortOutput;
            }

            RemoteRttiType output = new RemoteRttiType(app, typeDump.FullTypeName, typeDump.Assembly);

            // Temporarily indicate we are on-going creation
            _onGoingCreations[new Tuple<string, string>(typeDump.Assembly, typeDump.FullTypeName)] = output;

            string parentType = typeDump.ParentFullTypeName;
            if (parentType != null)
            {
                Lazy<Type> parent = new Lazy<Type>(() =>
                {
                    try
                    {
                        return Create(app, parentType, typeDump.ParentAssembly);

                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine("Failed to dump parent type: " + parentType);
                        Debug.WriteLine(ex.ToString());
                        return null;
                    }
                });
                output.SetParent(parent);
            }
            AddMembers(app, typeDump, output);

            // remove on-going creation indication
            _onGoingCreations.Remove(new Tuple<string, string>(typeDump.Assembly, typeDump.FullTypeName));

            // Register at resolver
            _resolver.RegisterType(output);

            return output;
        }

        private void AddMembers(RemoteApp app, TypeDump typeDump, RemoteRttiType output)
        {
            AddGroupOfFunctions(app, typeDump, typeDump.Methods, output, areConstructors: false);
            AddGroupOfFunctions(app, typeDump, typeDump.Constructors, output, areConstructors: true);
            AddFields(app, typeDump.Fields, output);

            foreach (TypeDump.TypeMethodTable methodTable in typeDump.MethodTables)
            {
                var methodTableInfo = new RemoteRttiMethodTableInfo(output,
                    methodTable.UndecoratedFullName,
                    methodTable.DecoratedName,
                    methodTable.XoredAddress ^ TypeDump.TypeMethodTable.XorMask);
                output.AddMethodTable(methodTableInfo);
            }
        }

        private void AddFields(RemoteApp app, List<TypeDump.TypeField> typeDumpFields, RemoteRttiType output)
        {
            foreach (TypeDump.TypeField typeDumpField in typeDumpFields)
            {
                var fi = new RemoteFieldInfo(output, typeof(nuint), typeDumpField.Name);
                output.AddField(fi);
            }
        }

        private static void AddGroupOfFunctions(RemoteApp app, TypeDump typeDump, List<TypeDump.TypeMethod> functions, RemoteRttiType declaringType, bool areConstructors)
        {
            foreach (TypeDump.TypeMethod func in functions)
            {
                AddFunctionImpl(app, typeDump, func, declaringType, areConstructors);
            }
        }

        public static MethodInfo AddFunctionImpl(RemoteApp app, TypeDump typeDump, TypeDump.TypeMethod func, RemoteRttiType declaringType, bool areConstructors)
        {
            string mangledName = func.DecoratedName;
            if (string.IsNullOrEmpty(mangledName))
                mangledName = func.Name;

            string moduleName = typeDump.Assembly;

            List<LazyRemoteParameterResolver> parameters = new List<LazyRemoteParameterResolver>(func.Parameters.Count);
            int i = 1;
            foreach (TypeDump.TypeMethod.MethodParameter restarizedParameter in func.Parameters)
            {
                string fullTypeName = restarizedParameter.FullTypeName;                

                string fakeParamName = $"a{i}";
                i++;
                Lazy<Type> paramFactory = CreateTypeFactory(fullTypeName, moduleName);
                LazyRemoteTypeResolver paramTypeResolver = new LazyRemoteTypeResolver(paramFactory,
                    //methodParameter.Assembly,
                    //methodParameter.FullTypeName,
                    //methodParameter.TypeName
                    null,
                    fullTypeName,
                    fullTypeName
                );
                LazyRemoteParameterResolver paramResolver =
                    new LazyRemoteParameterResolver(paramTypeResolver, fakeParamName);
                parameters.Add(paramResolver);
            }

            Lazy<Type> returnTypeFactory = CreateTypeFactory(func.ReturnTypeFullName ?? func.ReturnTypeName, moduleName);
            LazyRemoteTypeResolver returnTypeResolver = new LazyRemoteTypeResolver(returnTypeFactory,
                null,
                func.ReturnTypeFullName,
                func.ReturnTypeName);

            if (areConstructors)
            {
                // TODO: RTTI ConstructorsType
                LazyRemoteTypeResolver declaringTypeResolver = new LazyRemoteTypeResolver(declaringType);
                RemoteRttiConstructorInfo ctorInfo =
                    new RemoteRttiConstructorInfo(declaringTypeResolver, parameters.ToArray());
                declaringType.AddConstructor(ctorInfo);
                return ctorInfo;
            }
            else
            {
                // Regular method

                // Actual declaring type might be one of the parents of the current type
                LazyRemoteTypeResolver declaringTypeResolver = new LazyRemoteTypeResolver(declaringType);

                // Not using declaringType.FullName because it contains the module name as well
                string declaringTypeNameWithNamespace = $"{declaringType.Namespace}::{declaringType.Name}";
                if (!func.UndecoratedFullName.StartsWith(declaringTypeNameWithNamespace) && func.UndecoratedFullName.Contains("::"))
                {
                    string type = func.UndecoratedFullName.Substring(0, func.UndecoratedFullName.LastIndexOf("::"));
                    Lazy<Type> declaringTypeFactory = CreateTypeFactory(type, moduleName);
                    declaringTypeResolver = new LazyRemoteTypeResolver(declaringType);
                }

                RemoteRttiMethodInfo methodInfo =
                    new RemoteRttiMethodInfo(declaringTypeResolver, returnTypeResolver, func.Name, mangledName,
                        parameters.ToArray(), (MethodAttributes)func.Attributes);
                declaringType.AddMethod(methodInfo);
                return methodInfo;
            }

            Lazy<Type> CreateTypeFactory(string namespaceAndTypeName, string moduleName)
            {
                // Get rid of '*' in pointers so it's NOT treated as a wildcard
                string originalNamespaceAndTypeName = namespaceAndTypeName;
                namespaceAndTypeName = namespaceAndTypeName.TrimEnd('*');
                // Get rid of '&' in references
                namespaceAndTypeName = namespaceAndTypeName.TrimEnd('&');
                int pointerLevel = originalNamespaceAndTypeName.Length - namespaceAndTypeName.Length;
                // If no module name is given, use a wildcard
                moduleName ??= "*";

                var innerResolver = new Lazy<Type>(() =>
                {
                    if (_dotNetPrimitivesMap.TryGetValue(namespaceAndTypeName, out Type resultType))
                        return resultType;

                    if (_shittyCache.TryGetValue(namespaceAndTypeName, out resultType))
                        return resultType;

                    resultType = RttiTypesResolver.Instance.Resolve(typeDump.Assembly, $"{typeDump.Assembly}!{namespaceAndTypeName}");
                    if (resultType != null)
                        return resultType;

                    Debug.WriteLine($"[{DateTime.Now.ToLongTimeString()}][@@@][RttiTypeFactory] Trying to resolve non-cached sub-type: `{namespaceAndTypeName}`");

                    var possibleParamTypes = app.QueryTypes(namespaceAndTypeName).ToArray();
                    if (possibleParamTypes.Length != 1 && !namespaceAndTypeName.Contains('!'))
                    {
                        // Look for the "other" type in the current module
                        possibleParamTypes = app.QueryTypes($"{moduleName}!{namespaceAndTypeName}").ToArray();

                        // Fallback 1:  Look for matching IMPORTED types into the given module
                        // (Helpf the there are multiple types with the same name in several modules)
                        if (possibleParamTypes.Length != 1)
                        {
                            UnmanagedRemoteApp unmanApp = (UnmanagedRemoteApp)app;
                            possibleParamTypes = unmanApp.QueryTypes($"*!{namespaceAndTypeName}", importerModule: moduleName).ToArray();
                        }

                        // Fallback 2: Widen search to ALL loaded modules
                        if (possibleParamTypes.Length != 1 && moduleName != "*")
                        {
                            possibleParamTypes = app.QueryTypes($"*!{namespaceAndTypeName}").ToArray();
                        }
                    }

                    // Prefer any matches in the existing assembly
                    var paramTypeInSameAssembly =
                        possibleParamTypes.Where(t => t.Assembly == typeDump.Assembly).ToArray();
                    if (paramTypeInSameAssembly.Length > 0)
                    {
                        if (paramTypeInSameAssembly.Length > 1)
                        {
                            string candidates =
                                string.Join(", ",
                                    paramTypeInSameAssembly.Select(cand => $"{cand.Assembly}!{cand.TypeFullName}"));
                            throw new Exception(
                                $"Too many matches for the sub-type '{namespaceAndTypeName}' in the signature of {func.UndecoratedFullName} .\n" +
                                $"Candidates: " + candidates);
                        }

                        return app.GetRemoteType(paramTypeInSameAssembly.Single());
                    }



                    if (possibleParamTypes.Length == 0)
                    {
                        // No luck here. This might be defined in a different module
                        // OR and MORE LIKELY this is a pointer to some primitive.
                        // for example char* or int**.
                        Type temp = new DummyRttiType(originalNamespaceAndTypeName);
                        _shittyCache[originalNamespaceAndTypeName] = temp;
                        return temp;
                    }
                    if (possibleParamTypes.Length > 1)
                    {
                        // We have multiple matches and all narrowing down logic failed.
                        // We're using a dummy.
                        Type temp = new DummyRttiType(originalNamespaceAndTypeName);
                        _shittyCache[originalNamespaceAndTypeName] = temp;
                        return temp;
                    }

                    return app.GetRemoteType(possibleParamTypes.Single());
                });

                var pointerResolver = new Lazy<Type>(() =>
                {
                    Type inner = innerResolver.Value;
                    if (pointerLevel == 0)
                        return inner;
                    Type pointerType = PointerType.CreateRecursive(inner, pointerLevel);
                    return pointerType;
                });

                return pointerResolver;
            }
        }

        public static Dictionary<string, Type> _shittyCache = new Dictionary<string, Type>();

        // C++ types that were mapped to C# ones
        public static Dictionary<string, Type> _dotNetPrimitivesMap = new Dictionary<string, Type>()
        {
            ["char"] = typeof(char),
            ["bool"] = typeof(bool),
            ["int"] = typeof(int),
            ["long"] = typeof(long),
            ["short"] = typeof(short),
            ["float"] = typeof(float),
            ["double"] = typeof(double),
            ["void"] = typeof(void),
            ["void*"] = typeof(ulong),
            ["char*"] = typeof(CharStar),
            ["int64_t"] = typeof(long),
            ["uint64_t"] = typeof(ulong),
            ["int32_t"] = typeof(int),
            ["uint32_t"] = typeof(uint),
            ["int16_t"] = typeof(short),
            ["uint16_t"] = typeof(ushort)
        };
    }

    public class PointerType : Type
    {
        public override string Name => $"{Inner.Name}*";
        public override string FullName => $"{Inner.FullName}*";
        public override string Namespace => Inner.Namespace;
        public override Type BaseType => Inner.BaseType;
        public override Assembly Assembly => Inner.Assembly;
        public override string AssemblyQualifiedName => throw new NotImplementedException();
        public override Guid GUID => throw new NotImplementedException();
        public override Module Module => throw new NotImplementedException();
        public override Type UnderlyingSystemType => null;

        public Type Inner { get; set; }

        public PointerType(Type inner)
        {
            Inner = inner;
        }

        public static Type CreateRecursive(Type inner, int pointerLevel)
        {
            if (pointerLevel == 0)
                return inner;
            Type pointerType = new PointerType(inner);
            return CreateRecursive(pointerType, pointerLevel - 1);
        }

        protected override TypeAttributes GetAttributeFlagsImpl()
        {
            throw new NotImplementedException();
        }

        protected override ConstructorInfo GetConstructorImpl(BindingFlags bindingAttr, Binder binder, CallingConventions callConvention, Type[] types, ParameterModifier[] modifiers)
        {
            throw new NotImplementedException();
        }

        public override ConstructorInfo[] GetConstructors(BindingFlags bindingAttr)
        {
            throw new NotImplementedException();
        }

        public override Type GetElementType()
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

        public override FieldInfo GetField(string name, BindingFlags bindingAttr)
        {
            throw new NotImplementedException();
        }

        public override FieldInfo[] GetFields(BindingFlags bindingAttr)
        {
            throw new NotImplementedException();
        }

        [return: DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.Interfaces)]
        public override Type GetInterface(string name, bool ignoreCase)
        {
            throw new NotImplementedException();
        }

        public override Type[] GetInterfaces()
        {
            throw new NotImplementedException();
        }

        public override MemberInfo[] GetMembers(BindingFlags bindingAttr)
        {
            throw new NotImplementedException();
        }

        protected override MethodInfo GetMethodImpl(string name, BindingFlags bindingAttr, Binder binder, CallingConventions callConvention, Type[] types, ParameterModifier[] modifiers)
        {
            throw new NotImplementedException();
        }

        public override MethodInfo[] GetMethods(BindingFlags bindingAttr)
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

        public override PropertyInfo[] GetProperties(BindingFlags bindingAttr)
        {
            throw new NotImplementedException();
        }

        protected override PropertyInfo GetPropertyImpl(string name, BindingFlags bindingAttr, Binder binder, Type returnType, Type[] types, ParameterModifier[] modifiers)
        {
            throw new NotImplementedException();
        }

        protected override bool HasElementTypeImpl()
        {
            throw new NotImplementedException();
        }

        public override object InvokeMember(string name, BindingFlags invokeAttr, Binder binder, object target, object[] args, ParameterModifier[] modifiers, CultureInfo culture, string[] namedParameters)
        {
            throw new NotImplementedException();
        }

        protected override bool IsArrayImpl()
        {
            throw new NotImplementedException();
        }

        protected override bool IsByRefImpl()
        {
            throw new NotImplementedException();
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
    }
}