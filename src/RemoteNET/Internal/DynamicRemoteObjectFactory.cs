using RemoteNET.Internal.Reflection;
using ScubaDiver.API;
using ScubaDiver.API.Dumps;
using ScubaDiver.API.Utils;
using System;
using System.Collections.Generic;
using System.Linq;

namespace RemoteNET.Internal
{
    public class DynamicRemoteObjectFactory
    {
        private RemoteApp _app;

        public DynamicRemoteObject Create(RemoteApp rApp, RemoteObject remoteObj, TypeDump typeDump)
        {
            _app = rApp;
            DynamicRemoteObject dynRemoteObj = new DynamicRemoteObject(rApp, remoteObj);

            AddFields(rApp, remoteObj, typeDump, dynRemoteObj);
            AddEvents(rApp, remoteObj, typeDump, dynRemoteObj);
            AddProperties(rApp, remoteObj, typeDump, dynRemoteObj);
            AddMethods(rApp, remoteObj, typeDump, dynRemoteObj);

            return dynRemoteObj;
        }

        private Type GetDependentType(string fullTypeName, string assembly = null)
        {
            return _app.GetRemoteType(fullTypeName);
        }

        private void AddMethods(RemoteApp app, RemoteObject ro, TypeDump typeDump, DynamicRemoteObject dro)
        {
            // Adding methods
            // Gathering all methods from current type and all ancestor types
            List<TypeDump.TypeMethod> allMethods = new List<TypeDump.TypeMethod>();
            TypeDump currTypeDump = typeDump;
            while (currTypeDump != null)
            {
                // Adding but not overriding higher methods (from parents) if they have exact overrides in descendants
                foreach (var method in currTypeDump.Methods)
                {
                    if (allMethods.Any(existingMethod => existingMethod.SignaturesEqual(method)))
                    {
                        // Method was overloaded!
                        continue;
                    }
                    // Method not overloaded
                    allMethods.Add(method);
                }

                currTypeDump = currTypeDump.ParentDump;
            }
            // Adding all collected methods to the object
            foreach (TypeDump.TypeMethod methodInfo in allMethods)
            {
                if (methodInfo.ContainsGenericParameters)
                {
                    // TODO: Support generic methods. For now their parameters aren't cprrectly parse
                    // in the diver leaving us with missing types for them.
                    continue;
                }

                // Edge case: Sometimes we can only see the properties' "get_" and "set_" methos so we need to infere there's a property.
                if (methodInfo.Name.StartsWith("get_") || methodInfo.Name.StartsWith("set_"))
                {
                    string propName = methodInfo.Name.Substring(methodInfo.Name.IndexOf('_') + 1);
                    // Check if there's an explicit property with that name so we shouldn't worry about it.
                    if (!typeDump.Properties.Any(typeProp => typeProp.Name == propName))
                    {
                        // There isn't a property. Time to make one up.
                        var getMethod = allMethods.SingleOrDefault(typeMethod => typeMethod.Name == "get_" + propName);
                        var setMethod = allMethods.SingleOrDefault(typeMethod => typeMethod.Name == "set_" + propName);
                        // The full type name could be inffered from the result of the "get" method
                        // or the argument of the "set" method.
                        string typeFullName = null;
                        if (getMethod != null)
                        {
                            typeFullName = getMethod.ReturnTypeFullName;
                        }
                        else
                        {
                            typeFullName = setMethod.Parameters.First().Type;
                        }
                        AddProperty(app, ro, dro, propName, typeFullName,
                            getMethod != null,
                            setMethod != null);
                    }
                    // NOTE: In any case, we follow throu with adding this method as a it is reported.
                    // Just in case we are wrong and the remote object has some method like "get_some_things(...)"
                    // which doesn't represents a property (just funny naming conventions).
                }

                // Creating proxy method
                Func<object[], object> proxy = (args) =>
                {
                    ObjectOrRemoteAddress[] remoteParams = new ObjectOrRemoteAddress[args.Length];
                    for (int i = 0; i < args.Length; i++)
                    {
                        if (args[i] == null)
                        {
                            remoteParams[i] = ObjectOrRemoteAddress.Null;
                        }
                        else if (args[i] is RemoteObject remoteArg)
                        {
                            // Other remote object used as argument
                            remoteParams[i] = ObjectOrRemoteAddress.FromToken(remoteArg.RemoteToken, remoteArg.GetType().FullName);
                        }
                        else if (args[i] is DynamicRemoteObject droArg)
                        {
                            RemoteObject originRemoteObject = droArg.__ro;
                            remoteParams[i] = ObjectOrRemoteAddress.FromToken(originRemoteObject.RemoteToken, originRemoteObject.GetType().FullName);
                        }
                        else
                        {
                            // Argument from our own memory. Wrap it in ObjectOrRemoteAddress
                            // so it encodes it (as much as possible) for the diver to reconstruct (decode) on the other side
                            var wrapped = ObjectOrRemoteAddress.FromObj(args[i]);
                            remoteParams[i] = wrapped;
                        }
                    }

                    (bool hasResults, ObjectOrRemoteAddress returnedValue) = ro.InvokeMethod(methodInfo.Name, remoteParams);
                    if (!hasResults)
                    {
                        // Nothing was returned
                        return null;
                    }
                    else
                    {
                        if (!returnedValue.IsRemoteAddress)
                        {
                            // Returned a primitive - we can decode it here!
                            string encodedResults = returnedValue.EncodedObject;
                            return PrimitivesEncoder.Decode(encodedResults, returnedValue.Type);
                        }
                        else
                        {
                            var remoteObject = app.GetRemoteObject(returnedValue.RemoteAddress);
                            return remoteObject.Dynamify();
                        }
                    }
                };
                // TODO: Does this even work if any of the arguments is a remote one
                List<Tuple<Type,string>> argTypes = (from prmtr in methodInfo.Parameters
                                       let typeFullName = prmtr.Type
                                       let assm = prmtr.Assembly
                                       let resolvedType = GetDependentType(typeFullName, assm)
                                       select new Tuple<Type,string>(resolvedType,prmtr.Name)).ToList();
                Type retType = GetDependentType(methodInfo.ReturnTypeFullName, methodInfo.ReturnTypeAssembly);
                dro.AddMethod(methodInfo.Name, argTypes, retType, proxy);
            }
        }

        private void AddProperties(RemoteApp rApp, RemoteObject ro, TypeDump td, DynamicRemoteObject dro)
        {
            // Adding properties
            foreach (TypeDump.TypeProperty propInfo in td.Properties)
            {
                AddProperty(rApp, ro, dro, propInfo.Name, propInfo.TypeFullName,
                    propInfo.GetVisibility != null,
                    propInfo.SetVisibility != null);
            }
        }

        private void AddProperty(RemoteApp rApp, RemoteObject ro, DynamicRemoteObject dro, string name, string typeFullName, bool hasGetVisability, bool hasSetVisability)
        {
            if (dro.HasMember(name))
            {
                // Property already defined, let's skip it.
                Logger.Debug($"[AddProperty] PROPERTY {name} ALREADY DEFINED!");
                return;
            }

            Action<object> setter = null;
            Func<object> getter = null;
            // Check if there's even a getter in the remote process
            if (hasGetVisability)
            {
                // There's a getter! (some visibility means it exists. If it's missing GetVisibility = null)
                // Creating proxy method
                getter = new Func<object>(() =>
                {
                    // Non primitive property - getting remote object.
                    (bool hasResults, ObjectOrRemoteAddress res) = ro.InvokeMethod("get_" + name, new ObjectOrRemoteAddress[0]);
                    if (!hasResults)
                    {
                        throw new NotImplementedException("Trying to call getter of remote " +
                                                          "property but either hasResults was false");
                    }

                    if(res.IsNull)
                    {
                        return null;
                    }

                    if (res.IsRemoteAddress)
                    {
                        RemoteObject rObj = rApp.GetRemoteObject(res.RemoteAddress);
                        return rObj.Dynamify();
                    }
                    else
                    {
                        return PrimitivesEncoder.Decode(res.EncodedObject, typeFullName);
                    }
                });
            }
            // Check if there's even a setter in the remote process
            if (hasSetVisability)
            {
                // There's a setter! (some visibility means it exists. If it's missing GetVisibility = null)
                // Creating proxy method
                setter = (newValue) =>
                {
                    // Non primitive property - getting remote object.
                    (bool hasResults, ObjectOrRemoteAddress returnedValue) = ro.InvokeMethod("set_" + name, new ObjectOrRemoteAddress[] { ObjectOrRemoteAddress.FromObj(newValue) });
                    if (hasResults)
                    {
                        throw new NotImplementedException("Trying to call setter of remote " +
                                                          "property but for some reason it returned some results...");
                    }
                };
            }

            dro.AddProperty(name, typeFullName, getter, setter);
        }

        private void AddFields(RemoteApp rApp, RemoteObject ro, TypeDump td, DynamicRemoteObject dro)
        {
            foreach (TypeDump.TypeField fieldInfo in td.Fields)
            {
                // Edge case: Events show up as fields
                if (fieldInfo.TypeFullName == typeof(System.EventHandler).FullName)
                {
                    Type delegateType = GetDependentType(fieldInfo.TypeFullName, fieldInfo.Assembly);
                    System.Reflection.ParameterInfo[] delegateParams = delegateType.GetMethod("Invoke").GetParameters();
                    List<Type> paramTypes = delegateParams.Select(t => t.ParameterType).ToList();

                    dro.AddEvent(fieldInfo.Name, paramTypes);
                    continue;
                }

                Func<object> getter = () =>
                {
                    // Re-dumping field to get fresh value
                    ObjectOrRemoteAddress res = ro.GetField(fieldInfo.Name);
                    if (res.IsRemoteAddress)
                    {
                        var remoteObject = rApp.GetRemoteObject(res.RemoteAddress);
                        return remoteObject.Dynamify();
                    }
                    // Primitive
                    return PrimitivesEncoder.Decode(res.EncodedObject, fieldInfo.TypeFullName);
                };
                Action<object> setter = (newValue) =>
                {
                    ObjectOrRemoteAddress remoteNewVal = null;
                    switch (newValue)
                    {
                        case null:
                            remoteNewVal = ObjectOrRemoteAddress.Null;
                            break;
                        case RemoteObject remoteArg:
                            // Other remote object used as argument
                            remoteNewVal = ObjectOrRemoteAddress.FromToken(remoteArg.RemoteToken, remoteArg.GetType().FullName);
                            break;
                        case DynamicRemoteObject droArg:
                            RemoteObject originRemoteObject = droArg.__ro;
                            remoteNewVal = ObjectOrRemoteAddress.FromToken(originRemoteObject.RemoteToken, originRemoteObject.GetType().FullName);
                            break;
                        default:
                            // Argument from our own memory. Wrap it in ObjectOrRemoteAddress
                            // so it encodes it (as much as possible) for the diver to reconstruct (decode) on the other side
                            var wrapped = ObjectOrRemoteAddress.FromObj(newValue);
                            remoteNewVal = wrapped;
                            break;
                    }
                    ro.SetField(fieldInfo.Name, remoteNewVal);
                };
                dro.AddField(fieldInfo.Name, fieldInfo.TypeFullName, getter, setter);
            }
        }


        private void AddEvents(RemoteApp rApp, RemoteObject ro, TypeDump td, DynamicRemoteObject dro)
        {
            foreach (TypeDump.TypeEvent eventInfo in td.Events)
            {
                // Edge case: Events show up as fields
                Type delegateType = GetDependentType(eventInfo.TypeFullName);
                System.Reflection.ParameterInfo[] delegateParams = delegateType.GetMethod("Invoke").GetParameters();
                List<Type> paramTypes = delegateParams.Select(t => t.ParameterType).ToList();

                dro.AddEvent(eventInfo.Name, paramTypes);
                continue;
            }
        }

    }
}
