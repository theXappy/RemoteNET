using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using RemoteNET.Common;
using RemoteNET.Internal;
using ScubaDiver.API;
using ScubaDiver.API.Hooking;
using ScubaDiver.API.Utils;

namespace RemoteNET;

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
    private class MethodHooks : Dictionary<HookAction, PositionedLocalHook>
    {
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
        LocalHookCallback wrappedHook = WrapCallback(_app, hookAction);

        // Look for MethodHooks object for the given REMOTE OBJECT
        if (!_callbacksToProxies.ContainsKey(methodToHook))
        {
            _callbacksToProxies[methodToHook] = new MethodHooks();
        }
        MethodHooks methodHooks = _callbacksToProxies[methodToHook];

        if (methodHooks.ContainsKey(hookAction))
        {
            throw new NotImplementedException("Shouldn't use same hook for 2 patches of the same method");
        }
        if (methodHooks.Any(existingHook => existingHook.Value.Position == pos))
        {
            throw new NotImplementedException("Can not set 2 hooks in the same position on a single target");
        }

        methodHooks.Add(hookAction, new PositionedLocalHook(hookAction, wrappedHook, pos));

        List<string> parametersTypeFullNames;
        if (methodToHook is IRttiMethodBase rttiMethod)
        {
            // Skipping 'this'
            parametersTypeFullNames =
                rttiMethod.LazyParamInfos.Skip(1).Select(prm => prm.TypeResolver.TypeFullName).ToList();
        }
        else
        {
            parametersTypeFullNames =
                methodToHook.GetParameters().Select(prm => prm.ParameterType.FullName).ToList();
        }

        return HookMethod(methodToHook.DeclaringType.FullName, methodToHook.Name, pos, wrappedHook, parametersTypeFullNames);
    }

    private bool HookMethod(string typeFullName, string methodName, HarmonyPatchPosition pos, LocalHookCallback wrappedHook, List<string> parametersTypeFullNames)
    {
        return _app.Communicator.HookMethod(typeFullName, methodName, pos, wrappedHook, parametersTypeFullNames);
    }

    public static LocalHookCallback WrapCallback(RemoteApp app, HookAction callback)
    {
        LocalHookCallback hookProxy = (HookContext context, ObjectOrRemoteAddress instance, ObjectOrRemoteAddress[] args, ObjectOrRemoteAddress retValue) =>
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
                        RemoteObject roInstance = app.GetRemoteObject(oora);
                        o = roInstance.Dynamify();
                    }
                    catch (Exception)
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

            // Converting Return Value to DRO
            dynamic droRetValue;
            droRetValue = DecodeOora(retValue);


            object[] decodedParameters = new object[args.Length];
            for (int i = 0; i < decodedParameters.Length; i++)
            {
                dynamic item = DecodeOora(args[i] as ObjectOrRemoteAddress);
                decodedParameters[i] = item;
            }

            // Call the callback with the proxied parameters (using DynamicRemoteObjects)
            callback.DynamicInvoke(new object[4] { context, droInstance, decodedParameters, droRetValue });
        };
        return hookProxy;
    }

    public void Patch(MethodBase original,
        HookAction prefix = null,
        HookAction postfix = null,
        HookAction finalizer = null)
    {
        if (prefix == null &&
            postfix == null &&
            finalizer == null)
        {
            throw new ArgumentException("No hooks defined.");
        }

        if (prefix != null)
        {
            HookMethod(original, HarmonyPatchPosition.Prefix, prefix);
        }
        if (postfix != null)
        {
            HookMethod(original, HarmonyPatchPosition.Postfix, postfix);
        }
        if (finalizer != null)
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