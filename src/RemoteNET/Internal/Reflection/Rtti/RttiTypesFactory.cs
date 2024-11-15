using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using RemoteNET.Common;
using RemoteNET.Internal.Reflection;
using RemoteNET.Internal.Reflection.DotNet;
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


        public Type ResolveTypeWhileCreating(RemoteApp app, string typeInProgress, string methodName, string assembly, string type)
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
                            $"{nameof(RttiTypesFactory)} tried to dump type {type} when handling method {methodName} of type" +
                            $"{typeInProgress} but the {nameof(DiverCommunicator)}.{nameof(DiverCommunicator.DumpType)} function failed.");
                    }

                    Type newCreatedType = this.Create(app, dumpedArgType);
                    if (newCreatedType == null)
                    {
                        // remove on-going creation indication
                        throw new Exception(
                            $"{nameof(RttiTypesFactory)} tried to dump type {type} when handling method {methodName} of type" +
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
            Type shortOutput = _resolver.Resolve(typeDump.Assembly, typeDump.Type);
            if (shortOutput != null)
            {
                return shortOutput;
            }

            RemoteRttiType output = new RemoteRttiType(app, typeDump.Type, typeDump.Assembly);

            // Temporarily indicate we are on-going creation
            _onGoingCreations[new Tuple<string, string>(typeDump.Assembly, typeDump.Type)] = output;

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
            _onGoingCreations.Remove(new Tuple<string, string>(typeDump.Assembly, typeDump.Type));

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
                output.AddVftable(methodTable.Name, methodTable.Address);
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

        public static void AddFunctionImpl(RemoteApp app, TypeDump typeDump, TypeDump.TypeMethod func, RemoteRttiType declaringType, bool areConstructors)
        {
            string mangledName = func.DecoratedName;
            if (string.IsNullOrEmpty(mangledName))
                mangledName = func.Name;

            string moduleName = typeDump.Assembly;

            List<LazyRemoteParameterResolver> parameters = new List<LazyRemoteParameterResolver>(func.Parameters.Count);
            int i = 1;
            foreach (TypeDump.TypeMethod.MethodParameter restarizedParameter in func.Parameters)
            {
                // TODO: No support for methods with reference parameters for now.
                if (restarizedParameter.FullTypeName.EndsWith('&'))
                    return;

                string fakeParamName = $"a{i}";
                i++;
                Lazy<Type> paramFactory = CreateTypeFactory(restarizedParameter.FullTypeName, moduleName);
                LazyRemoteTypeResolver paramTypeResolver = new LazyRemoteTypeResolver(paramFactory,
                    //methodParameter.Assembly,
                    //methodParameter.FullTypeName,
                    //methodParameter.TypeName
                    null,
                    restarizedParameter.FullTypeName,
                    restarizedParameter.FullTypeName
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
            }
            else
            {
                // Regular method

                // Actual declaring type might be one of the parents of the current type
                LazyRemoteTypeResolver declaringTypeResolver = new LazyRemoteTypeResolver(declaringType);

                // Not using declaringType.FullName because it contains the module name as well
                string declaringTypeNameWithNamespace = $"{declaringType.Namespace}::{declaringType.Name}";
                if (!func.UndecoratedFullName.StartsWith(declaringTypeNameWithNamespace))
                {
                    string type = func.UndecoratedFullName.Substring(0, func.UndecoratedFullName.LastIndexOf("::"));
                    Lazy<Type> declaringTypeFactory = CreateTypeFactory(type, moduleName);
                    declaringTypeResolver = new LazyRemoteTypeResolver(declaringType);
                }

                RemoteRttiMethodInfo methodInfo =
                    new RemoteRttiMethodInfo(declaringTypeResolver, returnTypeResolver, func.Name, mangledName,
                        parameters.ToArray());
                declaringType.AddMethod(methodInfo);
            }

            Lazy<Type> CreateTypeFactory(string namespaceAndTypeName, string moduleName)
            {
                // Get rid of '*' in pointers so it's NOT treated as a wildcard
                string originalNamespaceAndTypeName = namespaceAndTypeName;
                namespaceAndTypeName = namespaceAndTypeName.TrimEnd('*');
                // If no module name is given, use a wildcard
                moduleName ??= "*";

                return new Lazy<Type>(() =>
                {
                    if (_shittyCache.TryGetValue(namespaceAndTypeName, out var t))
                        return t;

                    Debug.WriteLine($"[@@@][RttiTypeFactory] Trying to resolve non-cached sub-type: `{namespaceAndTypeName}`");

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

                    if (possibleParamTypes.Length == 0)
                    {
                        // No luck here. This might be defined in a different module
                        // OR and MORE LIKELY this is a pointer to some primitive.
                        // for example char* or int**.
                        Type temp = new DummyRttiType(originalNamespaceAndTypeName);
                        _shittyCache[originalNamespaceAndTypeName] = temp;
                        return temp;
                    }


                    var paramTypeInSameAssembly =
                        possibleParamTypes.Where(t => t.Assembly == typeDump.Assembly).ToArray();
                    if (paramTypeInSameAssembly.Length == 0)
                    {
                        return app.GetRemoteType(possibleParamTypes.Single());
                    }

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
                });
            }
        }

        public static Dictionary<string, Type> _shittyCache = new Dictionary<string, Type>();
    }
}