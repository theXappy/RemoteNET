using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Reflection;
using Reko.Core.Serialization;
using RemoteNET.Common;
using RemoteNET.Internal.Reflection;
using ScubaDiver.API;
using ScubaDiver.API.Interactions.Dumps;
using Reko.Environments.Windows;
using RemoteNET.RttiReflection.Demangle;

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
                    ManagedTypeDump dumpedArgType =
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

            ManagedTypeDump parentDump = _communicator.DumpType(fullTypeName, assembly);
            if (parentDump == null)
            {
                throw new Exception(
                    $"{nameof(RttiTypesFactory)} tried to dump type {fullTypeName} " +
                    $"but the {nameof(DiverCommunicator)}.{nameof(DiverCommunicator.DumpType)} function failed.");
            }
            return Create(app, parentDump);
        }

        public Type Create(RemoteApp app, ManagedTypeDump managedTypeDump)
        {
            Type shortOutput = _resolver.Resolve(managedTypeDump.Assembly, managedTypeDump.Type);
            if (shortOutput != null)
            {
                return shortOutput;
            }

            RemoteRttiType output = new RemoteRttiType(app, managedTypeDump.Type, managedTypeDump.Assembly);

            // Temporarily indicate we are on-going creation
            _onGoingCreations[new Tuple<string, string>(managedTypeDump.Assembly, managedTypeDump.Type)] = output;

            string parentType = managedTypeDump.ParentFullTypeName;
            if (parentType != null)
            {
                Lazy<Type> parent = new Lazy<Type>(() =>
                {
                    try
                    {
                        return Create(app, parentType, managedTypeDump.ParentAssembly);

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
            AddMembers(app, managedTypeDump, output);

            // remove on-going creation indication
            _onGoingCreations.Remove(new Tuple<string, string>(managedTypeDump.Assembly, managedTypeDump.Type));

            // Register at resolver
            _resolver.RegisterType(managedTypeDump.Assembly, managedTypeDump.Type, output);

            return output;
        }

        private void AddMembers(RemoteApp app, ManagedTypeDump managedTypeDump, RemoteRttiType output)
        {
            AddGroupOfFunctions(app, managedTypeDump, managedTypeDump.Methods, output, areConstructors: false);
            AddGroupOfFunctions(app, managedTypeDump, managedTypeDump.Constructors, output, areConstructors: true);
            //AddFields(app, managedTypeDump, output);
        }

        private void AddFields(RemoteApp app, ManagedTypeDump managedTypeDump, RemoteRttiType output)
        {
            throw new NotImplementedException();
        }

        private void AddGroupOfFunctions(RemoteApp app, ManagedTypeDump managedTypeDump, List<ManagedTypeDump.TypeMethod> functions, RemoteRttiType declaringType, bool areConstructors)
        {
            foreach (ManagedTypeDump.TypeMethod func in functions)
            {
                string? mangledName = func.MangledName;
                if(string.IsNullOrEmpty(mangledName))
                    mangledName = func.Name;

                List<ParameterInfo> parameters = new List<ParameterInfo>(func.Parameters.Count);
                int i = 1;
                foreach (ManagedTypeDump.TypeMethod.MethodParameter restarizedParameter in func.Parameters)
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
                    return new DummyGenericType(func.ReturnTypeFullName);
                });
                LazyRemoteTypeResolver returnTypeResolver = new LazyRemoteTypeResolver(returnTypeFactory,
                    null,
                    func.ReturnTypeFullName,
                    func.ReturnTypeFullName);

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