using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using RemoteNET.Internal;
using RemoteNET.Internal.Reflection;
using ScubaDiver;
using ScubaDiver.API;
using ScubaDiver.Extensions;
using ScubaDiver.Utils;

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

        public new Type GetType()
        {
            if (_type == null)
            {
                RemoteTypesFactory rtFactory = new RemoteTypesFactory(TypesResolver.Instance);
                rtFactory.AllowOwnDumping(_ref.Communicator);
                _type = rtFactory.Create(_ref.GetTypeDump());
            }

            return _type;
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
                    fieldDump = _ref.GetField(fieldInfo.Name);
                }
                catch (Exception e)
                {
                    Console.WriteLine($"[WARN] Field `{fieldInfo}` could not be retrieved. Error: "+e);
                    continue;
                }
                if (fieldDump.HasEncodedValue)
                {
                    object value = PrimitivesEncoder.Decode(fieldDump.EncodedValue, fieldInfo.TypeFullName);
                    dro.AddField(fieldDump.Name, value);
                }
                else
                {
                    // TODO: This is a non-primitive object so it's not encoded...
                    // Don't know what to do here yet.
                    // Skipping this field for now...
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
                            //throw new NotImplementedException("Can't get non-primitive properties yet");
                        }
                    });
                }
                // Check if there's even a setter in the remote process
                if (propInfo.SetVisibility != null)
                {
                    // There's a setter! (some visibility means it exists. If it's missing GetVisibility = null)
                    // Creating proxy method
                    setter = (obj) =>
                    {
                        throw new NotImplementedException("Setter exists but setting remote properties not implemented yet.");
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
                        Console.WriteLine($"@@@ Method {method.Name} seems to be overloaded!");
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
                     ObjectOrRemoteAddress[] encodedArgs = new ObjectOrRemoteAddress[args.Length];
                     for (int i = 0; i < args.Length; i++)
                     {
                         if (args[i] == null)
                         {
                             encodedArgs[i] = ObjectOrRemoteAddress.Null;
                         }
                         else if (args[i] is RemoteObject remoteArg)
                         {
                             // Other remote object used as argument
                             encodedArgs[i] = ObjectOrRemoteAddress.FromToken(remoteArg._ref.Token, remoteArg._ref.GetTypeDump().Type);
                         }
                         else
                         {
                             // Argument from our own memory. Wrap it in ObjectOrRemoteAddress
                             // so it encodes it (as much as possible) for the diver to reconstruct (decode) on the other side
                             var wrapped = ObjectOrRemoteAddress.FromObj(args[i]);
                             encodedArgs[i] = wrapped;
                         }
                     }

                     InvocationResults res = _ref.InvokeMethod(methodInfo.Name, encodedArgs);
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
                             // TODO: Create RemoteObject for results? this implies creaing an IDisposable which the
                             // user might not think should be disposed...
                             throw new NotImplementedException(
                                 $"Returned value from invocation of the method `{methodInfo.Name}` " +
                                 $"was a non-primitive object and currently these types are not supported. " +
                                 $"Returned type: {res.ReturnedObjectOrAddress.Type}");
                         }
                     }
                 };
                Debug.WriteLine("@@@ HANDLING METHOD: " + methodInfo.ToString());
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
            _ref.RemoteRelease();
        }

        public override string ToString()
        {
            return $"RemoteObject. Type: {_type.FullName ?? "UNK"} Reference: [{_ref}]";
        }
    }
}
