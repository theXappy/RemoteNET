using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Reflection;
using RemoteNET.Common;
using RemoteNET.Internal.Reflection;
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
            _resolver.RegisterType(typeDump.Assembly, typeDump.Type, output);

            return output;
        }

        private void AddMembers(RemoteApp app, TypeDump typeDump, RemoteRttiType output)
        {
            AddGroupOfFunctions(app, typeDump, typeDump.Methods, output, areConstructors: false);
            AddGroupOfFunctions(app, typeDump, typeDump.Constructors, output, areConstructors: true);
            //AddFields(app, managedTypeDump, output);
        }

        private void AddFields(RemoteApp app, TypeDump typeDump, RemoteRttiType output)
        {
            throw new NotImplementedException();
        }

        private void AddGroupOfFunctions(RemoteApp app, TypeDump typeDump, List<TypeDump.TypeMethod> functions, RemoteRttiType declaringType, bool areConstructors)
        {
            foreach (TypeDump.TypeMethod func in functions)
            {
                string? mangledName = func.DecoratedName;
                if(string.IsNullOrEmpty(mangledName))
                    mangledName = func.Name;

                List<ParameterInfo> parameters = new List<ParameterInfo>(func.Parameters.Count);
                int i = 1;
                foreach (TypeDump.TypeMethod.MethodParameter restarizedParameter in func.Parameters)
                {
                    string fakeParamName = $"a{i}";
                    i++;
                    Lazy<Type> paramFactory = new Lazy<Type>(() =>
                    {
                        // TODO: Actual resolve
                        return new DummyGenericType(restarizedParameter.FullTypeName);
                    });
                    LazyRemoteTypeResolver paramTypeResolver = new LazyRemoteTypeResolver(paramFactory,
                                   //methodParameter.Assembly,
                                   //methodParameter.FullTypeName,
                                   //methodParameter.TypeName
                                   null,
                                   restarizedParameter.FullTypeName,
                                   restarizedParameter.FullTypeName
                                   );
                    RemoteParameterInfo rpi = new RemoteParameterInfo(fakeParamName, paramTypeResolver);
                    parameters.Add(rpi);
                }

                Lazy<Type> returnTypeFactory = new Lazy<Type>(() =>
                {
                    // TODO: Actual resolve
                    return new DummyRttiType(func.ReturnTypeFullName ?? func.ReturnTypeName);
                });
                LazyRemoteTypeResolver returnTypeResolver = new LazyRemoteTypeResolver(returnTypeFactory,
                    null,
                    func.ReturnTypeFullName,
                    func.ReturnTypeName);

                if (areConstructors)
                {
                    // TODO: RTTI Constructors
                    RemoteRttiConstructorInfo ctorInfo =
                        new RemoteRttiConstructorInfo(declaringType, parameters.ToArray());
                    declaringType.AddConstructor(ctorInfo);
                }
                else
                {
                    // Regular method
                    RemoteRttiMethodInfo methodInfo =
                        new RemoteRttiMethodInfo(declaringType, returnTypeResolver, func.Name, mangledName, parameters.ToArray());
                    declaringType.AddMethod(methodInfo);
                }
            }

        }
    }
}