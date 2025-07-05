using RemoteNET.Common;
using RemoteNET.Internal;
using RemoteNET.RttiReflection;
using ScubaDiver.API;
using ScubaDiver.API.Hooking;
using ScubaDiver.API.Utils;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reflection;

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
        public DynamifiedHookCallback HookAction { get; set; }
        public LocalHookCallback WrappedHookAction { get; private set; }
        public HarmonyPatchPosition Position { get; private set; }
        public PositionedLocalHook(DynamifiedHookCallback action, LocalHookCallback callback, HarmonyPatchPosition pos)
        {
            HookAction = action;
            WrappedHookAction = callback;
            Position = pos;
        }
    }
    private class MethodHooks : Dictionary<DynamifiedHookCallback, PositionedLocalHook>
    {
    }


    public RemoteHookingManager(RemoteApp app)
    {
        _app = app;
        _isUnmanaged = _app.GetType().Name == "UnmanagedRemoteApp";
        _callbacksToProxies = new Dictionary<MethodBase, MethodHooks>();
    }

    /// <returns>True on success, false otherwise</returns>

    public bool HookMethod(MethodBase methodToHook, HarmonyPatchPosition pos, DynamifiedHookCallback hookAction)
    {
        // Wrapping the callback which uses `dynamic`s in a callback that handles `ObjectOrRemoteAddresses`
        // and converts them to DROs
        LocalHookCallback wrappedHook = WrapCallback(hookAction);

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

        return _app.Communicator.HookMethod(methodToHook.DeclaringType.FullName, methodToHook.Name, pos, wrappedHook, parametersTypeFullNames);
    }

    private LocalHookCallback WrapCallback(DynamifiedHookCallback hookAction)
    {
        LocalHookCallback hookProxy = (HookContext context, ObjectOrRemoteAddress instance, ObjectOrRemoteAddress[] args, ref ObjectOrRemoteAddress retValue) =>
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
            hookAction(context, droInstance, decodedParameters, ref droRetValue );

            retValue = UnmanagedRemoteFunctionsInvokeHelper.CreateRemoteParameter(droRetValue);
        };
        return hookProxy;
    }

    public void Patch(MethodBase original,
        DynamifiedHookCallback prefix = null,
        DynamifiedHookCallback postfix = null,
        DynamifiedHookCallback finalizer = null)
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

    public bool UnhookMethod(MethodBase methodToHook, DynamifiedHookCallback callback)
    {
        if (!_callbacksToProxies.TryGetValue(methodToHook, out MethodHooks hooks))
        {
            return false;
        }

        if (!hooks.TryGetValue(callback, out PositionedLocalHook positionedHookWrapper))
        {
            return false;
        }

        _app.Communicator.UnhookMethod(positionedHookWrapper.WrappedHookAction);
        hooks.Remove(callback);
        if (hooks.Count == 0)
        {
            // It was the last hook for this method, need to remove the inner dict
            _callbacksToProxies.Remove(methodToHook);
        }
        return true;
    }



}