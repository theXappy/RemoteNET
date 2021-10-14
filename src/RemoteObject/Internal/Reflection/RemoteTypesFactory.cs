using System;
using System.Collections.Generic;
using System.Data;
using System.Reflection;
using ScubaDiver;

namespace RemoteObject.Internal.Reflection
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
                            throw new Exception(
                                $"Could not resolve type {methodParameter.Type} using the function {nameof(ResolveTypeWhileCreating)} " +
                                $"and it did not throw any exceptions (returned NULL).");
                        }
                    }
                    catch
                    {
                        // remove on-going creation indication
                        _onGoingCreations.Remove(new Tuple<string, string>(typeDump.Assembly, typeDump.Type));
                        throw;
                    }

                    RemoteParameterInfo rpi = new RemoteParameterInfo(methodParameter.Name, paramType);
                    parameters.Add(rpi);
                }

                Type returnType = ResolveTypeWhileCreating(typeDump.Type, methodDump.Name,
                    methodDump.ReturnTypeAssembly, methodDump.ReturnTypeFullName);

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