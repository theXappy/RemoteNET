using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using ScubaDiver.API;
using ScubaDiver.API.Hooking;
using ScubaDiver.API.Utils;

namespace RemoteNET.Common
{
    public delegate void HookAction(HookContext context, dynamic instance, dynamic[] args);

    public class RemoteHookingManager
    {
        private readonly RemoteApp _app;
        private bool _isUnmanaged;

        private readonly Dictionary<MethodBase, MethodHooks> _callbacksToProxies;

        /// <summary>
        /// A LocalHookCallback in a specific patching position
        /// </summary>
        private class PositionedLocalHook
        {
            public HookAction HookAction { get; set; }
            public LocalHookCallback WrappedHookActio { get; private set; }
            public HarmonyPatchPosition Position { get; private set; }
            public PositionedLocalHook(HookAction action, LocalHookCallback callback, HarmonyPatchPosition pos)
            {
                HookAction = action;
                WrappedHookActio = callback;
                Position = pos;
            }
        }
        private class MethodHooks : Dictionary<HookAction, PositionedLocalHook> { 
        }


        public RemoteHookingManager(RemoteApp app)
        {
            _app = app;
            _isUnmanaged = _app.GetType().Name == "UnmanagedRemoteApp";
            _callbacksToProxies = new Dictionary<MethodBase, MethodHooks>();
        }

        /// <returns>True on success, false otherwise</returns>

        public bool HookMethod(MethodBase methodToHook, HarmonyPatchPosition pos, HookAction hookAction)
        {
            // Wrapping the callback which uses `dynamic`s in a callback that handles `ObjectOrRemoteAddresses`
            // and converts them to DROs
            LocalHookCallback wrappdHook = WrapCallback(hookAction);

            // Look for MethodHooks object for the given REMOTE OBJECT
            if (!_callbacksToProxies.ContainsKey(methodToHook))
            {
                _callbacksToProxies[methodToHook] = new MethodHooks();
            }
            else
            {
                throw new NotImplementedException("Setting multiple hooks on the same method is not implemented");
            }
            MethodHooks methodHooks = _callbacksToProxies[methodToHook];

            // 
            if(!methodHooks.ContainsKey(hookAction))
            {
                methodHooks.Add(hookAction, new PositionedLocalHook(hookAction, wrappdHook, pos));
            }
            else
            {
                throw new NotImplementedException("Shouldn't use same hook for 2 patches of the same method");
            }

            var parametersTypeFullNames = methodToHook.GetParameters().Select(prm => prm.ParameterType.FullName).ToList();
            return _app.Communicator.HookMethod(methodToHook.DeclaringType.FullName, methodToHook.Name, pos, wrappdHook, parametersTypeFullNames);
        }

        private LocalHookCallback WrapCallback(HookAction callback)
        {
            LocalHookCallback hookProxy = (HookContext context, ObjectOrRemoteAddress instance, ObjectOrRemoteAddress[] args) =>
            {
                dynamic DecodeOora(ObjectOrRemoteAddress oora)
                {
                    dynamic o;
                    if (oora.IsNull)
                    {
                        o = null;
                    }
                    else if (oora.IsRemoteAddress)
                    {
                        try
                        {
                            RemoteObject roInstance = this._app.GetRemoteObject(oora);
                            o = roInstance.Dynamify();
                        }
                        catch (Exception ex)
                        {
                            // HACK: If we failed to resolve a remote object, we just return it's pointer as a long...
                            o = oora.RemoteAddress;
                        }
                    }
                    else
                    {
                        o = PrimitivesEncoder.Decode(oora.EncodedObject, oora.Type);
                    }

                    return o;
                }

                // Converting instance to DRO
                dynamic droInstance;
                droInstance = DecodeOora(instance);


                object[] decodedParameters;
                if (_isUnmanaged)
                {
                    //
                    // PARSE UNMANAGED ARGUMENTS
                    //

                    decodedParameters = new object[args.Length];
                    for (int i = 0; i < decodedParameters.Length; i++)
                    {
                        dynamic item = DecodeOora(args[i] as ObjectOrRemoteAddress);
                        decodedParameters[i] = item;
                    }
                }
                else
                {
                    //
                    // PARSE MANAGED ARGUMENTS
                    //

                    // Converting args to DROs/raw primitive types
                    if (args.Length != 1)
                    {
                        throw new NotImplementedException("Unexpected arguments forwarded to callback from the diver.");
                    }

                    // We are expecting a single arg which is a REMOTE array of objects (object[]) and we need to flatten it
                    // into several (Dynamic) Remote Objects in a LOCAL array of objects.
                    RemoteObject ro = _app.GetRemoteObject(args[0]);
                    dynamic dro = ro.Dynamify();
                    if (!ro.GetRemoteType().IsArray)
                    {
                        throw new NotImplementedException(
                            "Unexpected arguments forwarded to callback from the diver -- single arg but not an array.");
                    }

                    int len = 0;
                    try
                    {
                        len = (int)dro.Length;
                    }
                    catch (Exception e)
                    {
                        Debug.WriteLine("ERROR ACCESSING ARRAY LEN: " + e);
                    }

                    decodedParameters = new object[len];
                    for (int i = 0; i < len; i++)
                    {
                        // Since this object isn't really a local array (just a proxy of a remote one) the index
                        // acceess causes a 'GetItem' function call and retrival of the remote object at the position
                        dynamic item = dro[i];
                        decodedParameters[i] = item;
                    }
                }

                // Call the callback with the proxied parameters (using DynamicRemoteObjects)
                callback.DynamicInvoke(new object[3] { context, droInstance, decodedParameters });
            };
            return hookProxy;
        }

        public void Patch(MethodBase original,
            HookAction prefix = null,
            HookAction postfix = null,
            HookAction finalizer = null)
        {
            if(prefix == null &&
                postfix == null &&
                finalizer == null)
            {
                throw new ArgumentException("No hooks defined.");
            }

            if(prefix != null)
            {
                HookMethod(original, HarmonyPatchPosition.Prefix, prefix);
            }
            if(postfix != null)
            {
                HookMethod(original, HarmonyPatchPosition.Postfix, postfix);
            }
            if(finalizer != null)
            {
                HookMethod(original, HarmonyPatchPosition.Finalizer, finalizer);
            }
        }

        public bool UnhookMethod(MethodBase methodToHook, HookAction callback)
        {
            if (!_callbacksToProxies.TryGetValue(methodToHook, out MethodHooks hooks))
            {
                return false;
            }

            if (!hooks.TryGetValue(callback, out PositionedLocalHook positionedHookWrapper))
            {
                return false;
            }

            _app.Communicator.UnhookMethod(positionedHookWrapper.WrappedHookActio);
            hooks.Remove(callback);
            if (hooks.Count == 0)
            {
                // It was the last hook for this method, need to remove the inner dict
                _callbacksToProxies.Remove(methodToHook);
            }
            return true;
        }



    }
}
