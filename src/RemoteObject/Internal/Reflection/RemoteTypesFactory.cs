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

        public Type Create(TypeDump td)
        {
            Type shortOutput = _resolver.Resolve(td.Assembly, td.Type);
            if (shortOutput != null)
            {
                return shortOutput;
            }

            RemoteType output = new RemoteType(td.Type, td.Assembly);

            // Temporarily indicate we are on-going creation
            _onGoingCreations[new Tuple<string, string>(td.Assembly, td.Type)] = output;

            foreach (TypeDump.TypeMethod tm in td.Methods)
            {
                List<ParameterInfo> parameters = new List<ParameterInfo>(tm.Parameters.Count);
                foreach (TypeDump.TypeMethod.MethodParameter methodParameter in tm.Parameters)
                {
                    // First: Search cache (which means local types & already-seen remote types)
                    Type paramType = _resolver.Resolve(methodParameter.Assembly, methodParameter.Name);

                    if (paramType == null)
                    {
                        // Second: Search types which are on-going creation 
                        if (!_onGoingCreations.TryGetValue(
                            new Tuple<string, string>(methodParameter.Assembly, methodParameter.Name), out paramType) || paramType == null)
                        {
                            // Third: Try to dump type (if we're allowed)
                            if (_communicator == null)
                            {
                                // remove on-going creation indication
                                _onGoingCreations.Remove(new Tuple<string, string>(td.Assembly, td.Type));
                                throw new NotImplementedException(
                                    $"Can not create {nameof(RemoteType)} for type {td.Type} because its " +
                                    $"method {tm.Name} contains a parameter of type {methodParameter.Name} which couldn't be resolved.\n" +
                                    $"This could be resolved by allowing {nameof(RemoteTypesFactory)} to dump types. See the {nameof(AllowOwnDumping)} method.");
                            }
                            else
                            {
                                TypeDump dumpedArgType =
                                    _communicator.DumpType(methodParameter.Type, methodParameter.Assembly);
                                if (dumpedArgType == null)
                                {
                                    // remove on-going creation indication
                                    _onGoingCreations.Remove(new Tuple<string, string>(td.Assembly, td.Type));
                                    throw new Exception(
                                        $"{nameof(RemoteTypesFactory)} tried to dump type {methodParameter.Type} when handling method {tm.Name} of type" +
                                        $"{td.Type} but the {nameof(DiverCommunicator)}.{nameof(DiverCommunicator.DumpType)} function failed.");
                                }

                                Type newCreatedType = this.Create(dumpedArgType);
                                if (newCreatedType == null)
                                {
                                    // remove on-going creation indication
                                    _onGoingCreations.Remove(new Tuple<string, string>(td.Assembly, td.Type));
                                    throw new Exception(
                                        $"{nameof(RemoteTypesFactory)} tried to dump type {methodParameter.Type} when handling method {tm.Name} of type" +
                                        $"{td.Type} but the inner {nameof(RemoteTypesFactory)}.{nameof(RemoteTypesFactory.Create)} function failed.");
                                }
                                paramType = newCreatedType;
                            }
                        }
                    }

                    RemoteParameterInfo rpi = new RemoteParameterInfo(methodParameter.Name, paramType);
                    parameters.Add(rpi);
                }

                RemoteMethodInfo method = new RemoteMethodInfo(tm.Name, output, parameters.ToArray());
                output.AddMethod(method);
            }

            // remove on-going creation indication
            _onGoingCreations.Remove(new Tuple<string, string>(td.Assembly, td.Type));

            // Register at resolver
            _resolver.RegisterType(td.Assembly, td.Type, output);

            return output;
        }

    }
}