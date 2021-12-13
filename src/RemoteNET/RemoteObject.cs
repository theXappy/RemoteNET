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

        private Dictionary<Delegate, DiverCommunicator.LocalEventCallback> _eventCallbacksAndProxies;

        public ulong RemoteToken => _ref.Token;

        internal RemoteObject(RemoteObjectRef reference, RemoteApp remoteApp)
        {
            _app = remoteApp;
            _ref = reference;
            _eventCallbacksAndProxies = new Dictionary<Delegate, DiverCommunicator.LocalEventCallback>();
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

        public ObjectOrRemoteAddress SetField(string fieldName, ObjectOrRemoteAddress newValue)
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
            // Adding fields 
            TypeDump typeDump = _ref.GetTypeDump();

            var factory = new DynamicRemoteObjectFactory();
            return factory.Create(_app, this, typeDump);
        }


        public void Dispose()
        {
            _ref?.RemoteRelease();
            _ref = null;
        }

        public override string ToString()
        {
            return $"RemoteObject. Type: {_type?.FullName ?? "UNK"} Reference: [{_ref}]";
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


            DiverCommunicator.LocalEventCallback callbackProxy = (ObjectOrRemoteAddress[] args) =>
            {
                DynamicRemoteObject[] droParameters = new DynamicRemoteObject[args.Length];
                for (int i = 0; i < args.Length; i++)
                {
                    RemoteObject ro = _app.GetRemoteObject(args[i].RemoteAddress);
                    DynamicRemoteObject dro = ro.Dynamify() as DynamicRemoteObject;

                    // This is a crucial part: set the DynamicRemoteObject (and it's parent RemoteObject)
                    // to now automaticlly dispose (which causes the remote parameters to be unpinned.)
                    // We must do this in case on the parameters was already an object we hold as a RemoteObject
                    // somewhere else in the program.
                    // Note how we exit this function WITHOUT calling "Dispose" on the RemoteObject
                    dro.DisableAutoDisposable();

                    droParameters[i] = dro;
                }

                // Call the callback wutg tge proxied parameters (using DynamicRemoteObjects)
                callback.DynamicInvoke(droParameters);

                // TODO: Change this so the callback can actually return stuff?
                return (true, null);
            };

            _eventCallbacksAndProxies[callback] = callbackProxy;

            _ref.EventSubscribe(eventName, callbackProxy);
        }
        public void EventUnsubscribe(string eventName, Delegate callback)
        {
            DiverCommunicator.LocalEventCallback callbackProxy;
            if (_eventCallbacksAndProxies.TryGetValue(callback, out callbackProxy))
            {
                _ref.EventUnsubscribe(eventName, callbackProxy);

                _eventCallbacksAndProxies.Remove(callback);
            }
        }
    }
}
