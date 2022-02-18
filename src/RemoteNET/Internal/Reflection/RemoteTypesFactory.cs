using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using ScubaDiver.API;
using ScubaDiver.API.Dumps;

namespace RemoteNET.Internal.Reflection
{
    public class RemoteTypesFactory
    {
        private readonly TypesResolver _resolver;
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
        /// types might depend on one another (circular references)
        /// </summary>
        private readonly Dictionary<Tuple<string, string>, Type> _onGoingCreations =
            new Dictionary<Tuple<string, string>, Type>();


        public Type ResolveTypeWhileCreating(RemoteApp app, string typeInProgress, string methodName, string assembly, string type)
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

                        Type newCreatedType = this.Create(app, dumpedArgType);
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



        public Type Create(RemoteApp app, TypeDump typeDump)
        {
            Type shortOutput = _resolver.Resolve(typeDump.Assembly, typeDump.Type);
            if (shortOutput != null)
            {
                return shortOutput;
            }

            RemoteType output = new RemoteType(app, typeDump.Type, typeDump.Assembly, typeDump.IsArray);

            // Temporarily indicate we are on-going creation
            _onGoingCreations[new Tuple<string, string>(typeDump.Assembly, typeDump.Type)] = output;
            AddGroupOfFunctions(app, typeDump, typeDump.Methods, output, areConstructors: false);
            AddGroupOfFunctions(app, typeDump, typeDump.Constructors, output, areConstructors: true);
            AddFields(app, typeDump, output);
            AddProperties(app, typeDump, output);

            AttachAccessorsToProperties(output);

            // remove on-going creation indication
            _onGoingCreations.Remove(new Tuple<string, string>(typeDump.Assembly, typeDump.Type));

            // Register at resolver
            _resolver.RegisterType(typeDump.Assembly, typeDump.Type, output);

            return output;
        }

        private void AttachAccessorsToProperties(RemoteType output)
        {
            MethodInfo[] methods = output.GetMethods();
            foreach (PropertyInfo pi in output.GetProperties())
            {
                RemotePropertyInfo rpi = pi as RemotePropertyInfo;
                MethodInfo getter = methods.FirstOrDefault(mi => mi.Name == "get_" + pi.Name);
                rpi.GetMethod = getter as RemoteMethodInfo;
                MethodInfo setter = methods.FirstOrDefault(mi => mi.Name == "set_" + pi.Name);
                rpi.SetMethod = setter as RemoteMethodInfo;
            }
        }

        private void AddProperties(RemoteApp app, TypeDump typeDump, RemoteType output)
        {
            foreach (TypeDump.TypeProperty propDump in typeDump.Properties)
            {
                Type returnType;
                try
                {
                    returnType = ResolveTypeWhileCreating(app, typeDump.Type, "prop__resolving__logic",
                    propDump.Assembly, propDump.TypeFullName);
                }
                catch (Exception e)
                {
                    Debug.WriteLine($"[RemoteTypesFactory] failed to create field {propDump.Name} because it's type couldn't be created.\n" +
                                    "The throw exception was: " + e);
                    continue;
                }

                RemotePropertyInfo propInfo = new RemotePropertyInfo(output, returnType, propDump.Name);
                output.AddProperty(propInfo);
            }
        }
        private void AddFields(RemoteApp app, TypeDump typeDump, RemoteType output)
        {
            foreach (TypeDump.TypeField fieldDump in typeDump.Fields)
            {
                Type returnType;
                try
                {
                    returnType = ResolveTypeWhileCreating(app, typeDump.Type, "field__resolving__logic",
                    fieldDump.Assembly, fieldDump.TypeFullName);
                }
                catch (Exception e)
                {
                    Debug.WriteLine($"[RemoteTypesFactory] failed to create field {fieldDump.Name} because it's type couldn't be created.\n" +
                                    "The throw exception was: " + e);
                    continue;
                }

                RemoteFieldInfo fieldInfo = new RemoteFieldInfo(output, returnType, fieldDump.Name);
                output.AddField(fieldInfo);
            }
        }

        private void AddGroupOfFunctions(RemoteApp app, TypeDump typeDump, List<TypeDump.TypeMethod> functions, RemoteType declaringType, bool areConstructors)
        {
            foreach (TypeDump.TypeMethod func in functions)
            {
                if (func.ContainsGenericParameters)
                {
                    Debug.Write($"[RemoteTypesFactory] Skipping method {func.Name} of {typeDump.Type} because it contains generic parameters.");
                    continue;
                }
                List<ParameterInfo> parameters = new List<ParameterInfo>(func.Parameters.Count);
                foreach (TypeDump.TypeMethod.MethodParameter methodParameter in func.Parameters)
                {
                    // First: Search cache (which means local types & already-seen remote types)
                    Type paramType = null;
                    try
                    {
                        paramType = ResolveTypeWhileCreating(app, typeDump.Type, func.Name, methodParameter.Assembly,
                            methodParameter.Type);
                        if (paramType == null)
                        {
                            // TODO: Add stub method to indicate this error to the users?
                            Debug.WriteLine(
                                $"[RemoteTypesFactory] Could not resolve method {func.Name} of {methodParameter.Type} using the function {nameof(ResolveTypeWhileCreating)} " +
                                $"and it did not throw any exceptions (returned NULL).");
                            continue;
                        }
                    }
                    catch (Exception e)
                    {
                        // TODO: Add stub method to indicate this error to the users?
                        Debug.WriteLine(
                            $"[RemoteTypesFactory] Could not resolve method {func.Name} of {methodParameter.Type} using the function {nameof(ResolveTypeWhileCreating)} " +
                            $"and it threw this exception: " + e);
                        continue;
                    }

                    RemoteParameterInfo rpi = new RemoteParameterInfo(methodParameter.Name, paramType);
                    parameters.Add(rpi);
                }

                Type returnType;
                try
                {
                    returnType = ResolveTypeWhileCreating(app, typeDump.Type, func.Name,
                    func.ReturnTypeAssembly, func.ReturnTypeFullName);
                }
                catch (Exception e)
                {
                    // TODO: This sometimes throws because of generic results (like List<SomeAssembly.SomeObject>)
                    Debug.WriteLine($"[RemoteTypesFactory] failed to create method {func.Name} because it's return type could be created.\n" +
                                    "The throw exception was: " + e);
                    // TODO: Add stub method to indicate this error to the users?
                    continue;
                }

                if (areConstructors)
                {
                    RemoteConstructorInfo ctorInfo =
                        new RemoteConstructorInfo(declaringType, parameters.ToArray());
                    declaringType.AddConstructor(ctorInfo);
                }
                else
                {
                    // Regular method
                    RemoteMethodInfo methodInfo =
                        new RemoteMethodInfo(declaringType, returnType, func.Name, parameters.ToArray());
                    declaringType.AddMethod(methodInfo);
                }
            }
        }
    }
}