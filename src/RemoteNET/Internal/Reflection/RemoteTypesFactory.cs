using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using ScubaDiver.API;
using ScubaDiver.API.Dumps;

namespace RemoteNET.Internal.Reflection
{
    public class RemoteTypesFactory
    {
        private readonly TypesResolver _resolver;
        private DiverCommunicator _communicator;

        private bool _avoidGenericsRecursion;

        public RemoteTypesFactory(TypesResolver resolver, bool avoidGenericsRecursion)
        {
            _resolver = resolver;

            // "Generic Recursion" is a made-up term which means for RemoteNET that Types that look like:
            //      SomeType< SomeType< SomeType< SomeType< SomeType .... >>>>
            // Are cut after the recognizing nested type in iteself many times (6+ occurences and at a very ong type name).
            // You might ask yourself where such an object is possible and if C# even allows definid such a type.
            // Well the example I found is within JetBra*ns "Platform.Core" assembly, where trying to dump some types result
            // in a dependent type which looks like:
            // JetBra*ns.DataFlow.PropertyChangedEventArgs`1[[
            //    JetBra*ns.DataFlow.PropertyChangedEventArgs`1[[
            //        JetBra*ns.DataFlow.PropertyChangedEventArgs`1[[
            //          JetBra*ns.DataFlow.PropertyChangedEventArgs`1[[
            //              JetBra*ns.DataFlow.BeforePropertyChangedEventArgs`1[[
            //              JetBra*ns.DataFlow.BeforePropertyChangedEventArgs`1[[
            //              JetBra*ns.DataFlow.BeforePropertyChangedEventArgs`1[[
            //              JetBra*ns.DataFlow.BeforePropertyChangedEventArgs`1[[
            //              JetBra*ns.DataFlow.BeforePropertyChangedEventArgs`1[[
            //          ...
            // I did not investigate this deeply but it seems very 
            _avoidGenericsRecursion = avoidGenericsRecursion;
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
            if(type.Length > 200)
            {
                // Only checking very long type names to reduce Regex executions
                if (type.Contains("[][][][][][]")) {
                    throw new Exception("Nestered self arrays types was detected and avoided.");
                }
            }
            if(type.Length > 500)
            {
                // Too long for any reasonable type
                throw new Exception("Incredibly long type names aren't supported.");
            }
            if(type.Contains("JetBrains.DataFlow.PropertyChangedEventArgs") && type.Length > 100) 
            {
                // Too long for any reasonable type
                throw new Exception("Incredibly long type names aren't supported.");
            }


            Type paramType = _resolver.Resolve(assembly, type);
            if (paramType != null)
            {
                // Either found in cache or found locally.
                
                // If it's a local type we need to wrap it in a "fake" RemoteType (So method invocations will actually 
                // happend in the remote app, for example)
                // (But not for primitives...)
                if (!(paramType is RemoteType) && !paramType.IsPrimitive)
                {
                    paramType = new RemoteType(app, paramType);
                    // TODO: Registring here in the cache is a hack but we couldn't register within "TypesResolver.Resolve"
                    // because we don't have the RemoteApp to associate the fake remote type with.
                    // Maybe this should move somewhere else...
                    _resolver.RegisterType(paramType);
                }
            }

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
            AddEvents(app, typeDump, output);

            // Enrich properties with getters and setters
            AttachAccessorsToProperties(output);

            // Enrich events with add/remove methods
            AttachAddAndRemoveToEvents(output);

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
                    Debug.WriteLine($"[RemoteTypesFactory] failed to create field {propDump.Name} because its type couldn't be created.\n" +
                                    "The throw exception was: " + e);
                    continue;
                }

                RemotePropertyInfo propInfo = new RemotePropertyInfo(output, returnType, propDump.Name);
                output.AddProperty(propInfo);
            }
        }

        private void AttachAddAndRemoveToEvents(RemoteType output)
        {
            MethodInfo[] methods = output.GetMethods();
            foreach (EventInfo ei in output.GetEvents())
            {
                RemoteEventInfo rpi = ei as RemoteEventInfo;
                MethodInfo add = methods.FirstOrDefault(mi => mi.Name == "add_" + ei.Name);
                rpi.AddMethod = add as RemoteMethodInfo;
                MethodInfo remove = methods.FirstOrDefault(mi => mi.Name == "remove_" + ei.Name);
                rpi.RemoveMethod = remove as RemoteMethodInfo;
            }
        }

        private void AddEvents(RemoteApp app, TypeDump typeDump, RemoteType output)
        {
            foreach (TypeDump.TypeEvent eventType in typeDump.Events)
            {
                Type eventHandlerType;
                try
                {
                    eventHandlerType = ResolveTypeWhileCreating(app, typeDump.Type, "event__resolving__logic", eventType.Assembly, eventType.TypeFullName);
                }
                catch (Exception e)
                {
                    Debug.WriteLine($"[RemoteTypesFactory] failed to create event {eventType.Name} because its type couldn't be created.\n" +
                                    "The throw exception was: " + e);
                    continue;
                }

                var eventInfo = new RemoteEventInfo(output, eventHandlerType, eventHandlerType.Name);
                output.AddEvent(eventInfo);
            }
        }

        private Type ResolveTypeWhileCreating(RemoteApp app, string type, string v, object assembly, string typeFullName)
        {
            throw new NotImplementedException();
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
                    Debug.WriteLine($"[RemoteTypesFactory] failed to create field {fieldDump.Name} because its type couldn't be created.\n" +
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
                List<ParameterInfo> parameters = new List<ParameterInfo>(func.Parameters.Count);
                foreach (TypeDump.TypeMethod.MethodParameter methodParameter in func.Parameters)
                {
                    // First: Search cache (which means local types & already-seen remote types)
                    Type paramType = null;
                    if (methodParameter.IsGenericParameter)
                    {
                        // In case of a generic type we have no way to "resolve" it
                        // We are just creating a dummy type
                        paramType = new RemoteType(app, typeDump.Type, "FakeAssemblyForGenericTypes", typeDump.IsArray, true);

                    }
                    else
                    {
                        // Non-generic parameter 
                        // Cases that will not arrive here:
                        //      void MyMethod<T>(T item)  <-- The 'item' parameter won't get here
                        // Cases that will arrive here:
                        //      void MyOtherMethod(System.Text.StringBuilder sb) <-- The 'sb' parameter WILL get here
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
                    Debug.WriteLine($"[RemoteTypesFactory] failed to create method {func.Name} because its return type could be created.\n" +
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
                        new RemoteMethodInfo(declaringType, returnType, func.Name, null, parameters.ToArray());
                    declaringType.AddMethod(methodInfo);
                }
            }
        }
    }
}