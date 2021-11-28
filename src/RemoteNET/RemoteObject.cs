using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using RemoteNET.Internal;
using RemoteNET.Internal.Reflection;
using ScubaDiver;
using ScubaDiver.API;
using ScubaDiver.API.Dumps;
using ScubaDiver.API.Extensions;
using ScubaDiver.API.Utils;

namespace RemoteNET
{
    public class RemoteObject : IDisposable
    {
        private RemoteApp _app;
        private RemoteObjectRef _ref;
        private Type _type = null;

        public ulong RemoteToken => _ref.Token;

        internal RemoteObject(RemoteObjectRef reference, RemoteApp remoteApp)
        {
            _app = remoteApp;
            _ref = reference;
        }

        /// <summary>
        /// Gets the type of the proxied remote object, in the remote app. (This does not reutrn `typeof(RemoteObject)`)
        /// </summary>
        public new Type GetType()
        {
            if (_type == null)
            {
                RemoteTypesFactory rtFactory = new RemoteTypesFactory(TypesResolver.Instance);
                rtFactory.AllowOwnDumping(_ref.Communicator);
                _type = rtFactory.Create(this._app, _ref.GetTypeDump());
            }

            return _type;
        }

        private ObjectOrRemoteAddress SetField(string fieldName, ObjectOrRemoteAddress newValue)
        {
            InvocationResults invokeRes = _ref.SetField(fieldName, newValue);
            return invokeRes.ReturnedObjectOrAddress;

        }
        public (bool hasResults, ObjectOrRemoteAddress returnedValue) InvokeMethod(string methodName,
            params ObjectOrRemoteAddress[] args)
        {
            InvocationResults invokeRes = _ref.InvokeMethod(methodName, args);
            if (invokeRes.VoidReturnType)
            {
                return (false, null);
            }
            return (true, invokeRes.ReturnedObjectOrAddress);
        }

        public dynamic Dynamify()
        {
            DynamicRemoteObject dro = new DynamicRemoteObject(this);
            // Adding fields 
            TypeDump typeDump = _ref.GetTypeDump();
            foreach (var fieldInfo in typeDump.Fields)
            {
                MemberDump fieldDump = null;
                try
                {
                    fieldDump = _ref.GetFieldDump(fieldInfo.Name);
                }
                catch (Exception e)
                {
                    Logger.Debug($"[WARN] Field `{fieldInfo}` could not be retrieved. Error: " + e);
                    continue;
                }
                if (fieldDump.HasEncodedValue)
                {
                    Func<object> getter = () =>
                    {
                        // Re-dumping field to get fresh value
                         fieldDump = _ref.GetFieldDump(fieldInfo.Name, refresh: true);
                        object value = PrimitivesEncoder.Decode(fieldDump.EncodedValue, fieldInfo.TypeFullName);
                        return value;
                    };
                    Action<object> setter = (newValue) =>
                    {
                        ObjectOrRemoteAddress newValOora = ObjectOrRemoteAddress.FromObj(newValue);
                        ObjectOrRemoteAddress reflectedValue = SetField(fieldDump.Name, newValOora);
                        // TODO: Return reflected field?
                    };
                    dro.AddField(fieldDump.Name, getter,setter);
                }
                else
                {
                    // TODO: This is a non-primitive object so it's not encoded...
                    // Don't know what to do here yet.
                    // Skipping this field for now...
                    Func<object> getter = () =>
                    {
                        // Re-dumping field to get fresh value
                        InvocationResults res = _ref.GetField(fieldInfo.Name);

                        if (!res.ReturnedObjectOrAddress.IsRemoteAddress)
                        {
                            throw new Exception($"Invoked {nameof(RemoteObjectRef)}.{nameof(RemoteObjectRef.GetField)} expecting " +
                                                $"an object but the result was not a remote address");
                        }

                        var remoteObject = _app.GetRemoteObject(res.ReturnedObjectOrAddress.RemoteAddress);
                        return remoteObject.Dynamify();
                    };
                    Action<object> setter = (newValue) =>
                    {
                        ObjectOrRemoteAddress remoteNewVal = null;
                        if (newValue == null)
                        {
                            remoteNewVal = ObjectOrRemoteAddress.Null;
                        }
                        else if (newValue is RemoteObject remoteArg)
                        {
                            // Other remote object used as argument
                            remoteNewVal = ObjectOrRemoteAddress.FromToken(remoteArg._ref.Token, remoteArg._ref.GetTypeDump().Type);
                        }
                        else if (newValue is DynamicRemoteObject droArg)
                        {
                            RemoteObject originRemoteObject = droArg.__ro;
                            remoteNewVal = ObjectOrRemoteAddress.FromToken(originRemoteObject.RemoteToken, originRemoteObject.GetType().FullName);
                        }
                        else
                        {
                            // Argument from our own memory. Wrap it in ObjectOrRemoteAddress
                            // so it encodes it (as much as possible) for the diver to reconstruct (decode) on the other side
                            var wrapped = ObjectOrRemoteAddress.FromObj(newValue);
                            remoteNewVal = wrapped;
                        }
                        ObjectOrRemoteAddress reflectedValue = SetField(fieldDump.Name, remoteNewVal);
                        // TODO: Return reflected field?
                    };
                    dro.AddField(fieldDump.Name, getter, setter);
                }
            }
            // Adding properties
            foreach (TypeDump.TypeProperty propInfo in typeDump.Properties)
            {
                Action<object> setter = null;
                Func<object> getter = null;
                // Check if there's even a getter in the remote process
                if (propInfo.GetVisibility != null)
                {
                    // There's a getter! (some visibility means it exists. If it's missing GetVisibility = null)
                    // Creating proxy method
                    getter = new Func<object>(() =>
                    {
                        MemberDump propDump = _ref.GetProperty(propInfo.Name);
                        if (propDump.HasEncodedValue)
                        {
                            object value = PrimitivesEncoder.Decode(propDump.EncodedValue, propInfo.TypeFullName);
                            return value;
                        }
                        else
                        {
                            // Non primitive property - getting remote object.
                            (bool hasResults, ObjectOrRemoteAddress returnedValue) = InvokeMethod("get_" + propInfo.Name, new ObjectOrRemoteAddress[0]);
                            if (!hasResults || !returnedValue.IsRemoteAddress)
                            {
                                throw new NotImplementedException("Trying to call getter of remote " +
                                                                  "property but either hasResults or " +
                                                                  "returnedValue.IsRemoteAddress were false");
                            }
                            RemoteObject rObj = _app.GetRemoteObject(returnedValue.RemoteAddress);
                            return rObj.Dynamify();
                        }
                    });
                }
                // Check if there's even a setter in the remote process
                if (propInfo.SetVisibility != null)
                {
                    // There's a setter! (some visibility means it exists. If it's missing GetVisibility = null)
                    // Creating proxy method
                    setter = (newValue) =>
                    {
                        // Non primitive property - getting remote object.
                        (bool hasResults, ObjectOrRemoteAddress returnedValue) = InvokeMethod("set_" + propInfo.Name, new ObjectOrRemoteAddress[] { ObjectOrRemoteAddress.FromObj(newValue) });
                        if (hasResults)
                        {
                            throw new NotImplementedException("Trying to call setter of remote " +
                                                              "property but for some reason it returned some results...");
                        }
                    };
                }

                dro.AddProperty(propInfo.Name, getter, setter);
            }

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
                             remoteParams[i] = ObjectOrRemoteAddress.FromToken(remoteArg._ref.Token, remoteArg._ref.GetTypeDump().Type);
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

                     InvocationResults res = _ref.InvokeMethod(methodInfo.Name, remoteParams);
                     if (res.VoidReturnType)
                     {
                         // Nothing was returned
                         return null;
                     }
                     else
                     {
                         if (!res.ReturnedObjectOrAddress.IsRemoteAddress)
                         {
                             // Returned a primitive - we can decode it here!
                             string encodedResults = res.ReturnedObjectOrAddress.EncodedObject;
                             return PrimitivesEncoder.Decode(encodedResults, res.ReturnedObjectOrAddress.Type);
                         }
                         else
                         {
                             var remoteObject = _app.GetRemoteObject(res.ReturnedObjectOrAddress.RemoteAddress);
                             return remoteObject.Dynamify();
                         }
                     }
                 };
                List<Type> argTypes = (from prmtr in methodInfo.Parameters
                                       let typeFullName = prmtr.Type
                                       let resolvedType = AppDomain.CurrentDomain.GetType(typeFullName)
                                       select resolvedType).ToList();
                dro.AddMethod(methodInfo.Name, argTypes, proxy);
            }

            return dro;
        }


        public void Dispose()
        {
            _ref?.RemoteRelease();
            _ref = null;
        }

        ~RemoteObject()
        {
            Dispose();

        }

        public override string ToString()
        {
            return $"RemoteObject. Type: {_type?.FullName ?? "UNK"} Reference: [{_ref}]";
        }
    }
}
