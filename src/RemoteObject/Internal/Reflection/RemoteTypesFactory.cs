using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Reflection;
using ScubaDiver;
using ScubaDiver.API;

namespace RemoteNET.Internal.Reflection
{
    public class RemoteTypesFactory
    {
        private TypesResolver _resolver;
        private DiverCommunicator _communicator;

        public RemoteTypesFactory(TypesResolver resolver)
        {
            _resolver = resolver;
        }

        /// <summary>
        /// Allows the factory to further dump remote types if needed when creating other remote types.
        /// </summary>
        public void AllowOwnDumping(DiverCommunicator com)
        {
            _communicator = com;
        }

        /// <summary>
        /// This collection marks which types the factory is currently creating
        /// it's important since <see cref="Create"/> might recursively call itself and
        /// types might depend on one another
        /// </summary>
        private Dictionary<Tuple<string, string>, Type> _onGoingCreations =
            new Dictionary<Tuple<string, string>, Type>();


        public Type ResolveTypeWhileCreating(string typeInProgress, string methodName, string assembly, string type)
        {
            Type paramType = _resolver.Resolve(assembly, type);

            if (paramType == null)
            {
                // Second: Search types which are on-going creation 
                if (!_onGoingCreations.TryGetValue(
                    new Tuple<string, string>(assembly, type), out paramType) || paramType == null)
                {
                    // Third: Try to dump type (if we're allowed)
                    if (_communicator == null)
                    {
                        throw new NotImplementedException(
                            $"Can not create {nameof(RemoteType)} for type {typeInProgress} because its " +
                            $"method {methodName} contains a parameter of type {type} which couldn't be resolved.\n" +
                            $"This could be resolved by allowing {nameof(RemoteTypesFactory)} to dump types. See the {nameof(AllowOwnDumping)} method.");
                    }
                    else
                    {
                        TypeDump dumpedArgType =
                            _communicator.DumpType(type, assembly);
                        if (dumpedArgType == null)
                        {
                            throw new Exception(
                                $"{nameof(RemoteTypesFactory)} tried to dump type {type} when handling method {methodName} of type" +
                                $"{typeInProgress} but the {nameof(DiverCommunicator)}.{nameof(DiverCommunicator.DumpType)} function failed.");
                        }

                        Type newCreatedType = this.Create(dumpedArgType);
                        if (newCreatedType == null)
                        {
                            // remove on-going creation indication
                            throw new Exception(
                                $"{nameof(RemoteTypesFactory)} tried to dump type {type} when handling method {methodName} of type" +
                                $"{typeInProgress} but the inner {nameof(RemoteTypesFactory)}.{nameof(RemoteTypesFactory.Create)} function failed.");
                        }
                        paramType = newCreatedType;
                    }
                }
            }
            return paramType;
        }

        public Type Create(TypeDump typeDump)
        {
            Type shortOutput = _resolver.Resolve(typeDump.Assembly, typeDump.Type);
            if (shortOutput != null)
            {
                return shortOutput;
            }

            RemoteType output = new RemoteType(typeDump.Type, typeDump.Assembly);

            // Temporarily indicate we are on-going creation
            _onGoingCreations[new Tuple<string, string>(typeDump.Assembly, typeDump.Type)] = output;

            foreach (TypeDump.TypeMethod methodDump in typeDump.Methods)
            {
                if (methodDump.ContainsGenericParameters)
                {
                    Debug.Write($"[RemoteTypesFactory] Skipping method {methodDump.Name} of {typeDump.Type} because it contains generic parameters.");
                    continue;
                }
                List<ParameterInfo> parameters = new List<ParameterInfo>(methodDump.Parameters.Count);
                foreach (TypeDump.TypeMethod.MethodParameter methodParameter in methodDump.Parameters)
                {
                    // First: Search cache (which means local types & already-seen remote types)
                    Type paramType = null;
                    try
                    {
                        paramType = ResolveTypeWhileCreating(typeDump.Type, methodDump.Name, methodParameter.Assembly,
                            methodParameter.Type);
                        if (paramType == null)
                        {
                            // TODO: Add stub method to indicate this error to the users?
                            Debug.WriteLine(
                                $"[RemoteTypesFactory] Could not resolve method {methodDump.Name} of {methodParameter.Type} using the function {nameof(ResolveTypeWhileCreating)} " +
                                $"and it did not throw any exceptions (returned NULL).");
                            continue;
                        }
                    }
                    catch (Exception e)
                    {
                        // TODO: Add stub method to indicate this error to the users?
                        Debug.WriteLine(
                            $"[RemoteTypesFactory] Could not resolve method {methodDump.Name} of {methodParameter.Type} using the function {nameof(ResolveTypeWhileCreating)} " +
                            $"and it threw this exception: " + e);
                        continue;
                    }

                    RemoteParameterInfo rpi = new RemoteParameterInfo(methodParameter.Name, paramType);
                    parameters.Add(rpi);
                }

                Type returnType;
                try
                {
                    returnType = ResolveTypeWhileCreating(typeDump.Type, methodDump.Name,
                    methodDump.ReturnTypeAssembly, methodDump.ReturnTypeFullName);
                }
                catch (Exception e)
                {
                    // TODO: This sometimes throws because of generic results (like List<SomeAssembly.SomeObject>)
                    Debug.WriteLine($"[RemoteTypesFactory] failed to create method {methodDump.Name} because it's return type could be created.\n" +
                                    "The throw exception was: " + e);
                    // TODO: Add stub method to indicate this error to the users?
                    continue;
                }

                RemoteMethodInfo methodInfo =
                    new RemoteMethodInfo(output, returnType, methodDump.Name, parameters.ToArray());
                output.AddMethod(methodInfo);
            }

            // remove on-going creation indication
            _onGoingCreations.Remove(new Tuple<string, string>(typeDump.Assembly, typeDump.Type));

            // Register at resolver
            _resolver.RegisterType(typeDump.Assembly, typeDump.Type, output);

            return output;
        }

    }
}