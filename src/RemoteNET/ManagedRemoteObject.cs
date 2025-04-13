using System;
using System.Collections.Generic;
using RemoteNET.Internal;
using ScubaDiver.API;
using ScubaDiver.API.Interactions;
using ScubaDiver.API.Interactions.Dumps;

namespace RemoteNET
{
    public class ManagedRemoteObject : RemoteObject
    {
        private static int NextIndex = 1;
        public int Index;

        private readonly RemoteApp _app;
        private RemoteObjectRef _ref;
        private Type _type = null;

        private readonly Dictionary<Delegate, DiverCommunicator.LocalEventCallback> _eventCallbacksAndProxies;

        public override ulong RemoteToken => _ref.Token;

        public override object App => _app;
        public override TypeDump GetTypeDump() => _ref.GetTypeDump();

        internal ManagedRemoteObject(RemoteObjectRef reference, RemoteApp remoteApp)
        {
            Index = NextIndex++;
            _app = remoteApp;
            _ref = reference;
            _eventCallbacksAndProxies = new Dictionary<Delegate, DiverCommunicator.LocalEventCallback>();
        }

        /// <summary>
        /// Gets the type of the proxied remote object, in the remote app. (This does not reutrn `typeof(RemoteObject)`)
        /// </summary>
        public new Type GetType() => GetRemoteType();
        public override Type GetRemoteType()
        {
            return _type ??= _app.GetRemoteType(_ref.GetTypeDump());
        }

        public ObjectOrRemoteAddress SetField(string fieldName, ObjectOrRemoteAddress newValue)
        {
            InvocationResults invokeRes = _ref.SetField(fieldName, newValue);
            return invokeRes.ReturnedObjectOrAddress;

        }
        public (bool hasResults, ObjectOrRemoteAddress returnedValue) InvokeMethod(string methodName,
            params ObjectOrRemoteAddress[] args)
            => InvokeMethod(methodName, args);

        public (bool hasResults, ObjectOrRemoteAddress returnedValue) InvokeMethod(string methodName,
            string[] genericArgsFullTypeNames,
            params ObjectOrRemoteAddress[] args)
        {
            InvocationResults invokeRes = _ref.InvokeMethod(methodName, genericArgsFullTypeNames, args);
            if (invokeRes.VoidReturnType)
            {
                return (false, null);
            }
            return (true, invokeRes.ReturnedObjectOrAddress);
        }

        ~ManagedRemoteObject()
        {
            _ref?.RemoteRelease();
            _ref = null;
        }

        public override string ToString()
        {
            return $"{nameof(ManagedRemoteObject)}. Type: {_type?.FullName ?? "UNK"} Reference: [{_ref}]";
        }

        public ObjectOrRemoteAddress GetField(string name)
        {
            var res = _ref.GetField(name);
            return res.ReturnedObjectOrAddress;
        }
        public void EventSubscribe(string eventName, Delegate callback)
        {
            // TODO: Add a check for amount of parameters and types (need to be dynamics)
            // See implementation inside DynamicEventProxy

            DiverCommunicator.LocalEventCallback callbackProxy = (ObjectOrRemoteAddress[] args, ObjectOrRemoteAddress retVal) =>
            {
                RemoteObject[] droParameters = new RemoteObject[args.Length];
                for (int i = 0; i < args.Length; i++)
                {
                    RemoteObject ro = _app.GetRemoteObject(args[i]);
                    droParameters[i] = ro;
                }
                RemoteObject droRetValue = _app.GetRemoteObject(retVal);

                // Call the callback with the proxied parameters (using DynamicRemoteObjects)
                callback.DynamicInvoke(droParameters, droRetValue);

                // TODO: Change this so the callback can actually return stuff?
                return (true, null);
            };

            _eventCallbacksAndProxies[callback] = callbackProxy;

            _ref.EventSubscribe(eventName, callbackProxy);
        }
        public void EventUnsubscribe(string eventName, Delegate callback)
        {
            if (_eventCallbacksAndProxies.TryGetValue(callback, out DiverCommunicator.LocalEventCallback callbackProxy))
            {
                _ref.EventUnsubscribe(eventName, callbackProxy);

                _eventCallbacksAndProxies.Remove(callback);
            }
        }

        public override ObjectOrRemoteAddress GetItem(ObjectOrRemoteAddress key)
        {
            return  _ref.GetItem(key);
        }

        public override RemoteObject Cast(Type t)
        {
            throw new NotImplementedException("Not implemented in Managed context");
        }
    }
}
