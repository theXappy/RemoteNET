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
        public ulong InstanceAddress { get; private set; }
        public PositionedLocalHook(DynamifiedHookCallback action, LocalHookCallback callback, HarmonyPatchPosition pos, ulong instanceAddress)
        {
            HookAction = action;
            WrappedHookAction = callback;
            Position = pos;
            InstanceAddress = instanceAddress;
        }
    }
    private class MethodHooks : Dictionary<DynamifiedHookCallback, PositionedLocalHook>
    {
    }
    
    // Cache for RemoteObject property reflection (thread-safe via lock)
    private static System.Reflection.PropertyInfo _remoteObjectProperty = null;
    private static readonly object _remoteObjectPropertyLock = new object();


    public RemoteHookingManager(RemoteApp app)
    {
        _app = app;
        _isUnmanaged = _app.GetType().Name == "UnmanagedRemoteApp";
        _callbacksToProxies = new Dictionary<MethodBase, MethodHooks>();
    }

    /// <returns>True on success, false otherwise</returns>

    public bool HookMethod(MethodBase methodToHook, HarmonyPatchPosition pos, DynamifiedHookCallback hookAction, RemoteObject instance = null)
    {
        // Extract instance address if provided
        ulong instanceAddress = 0;
        if (instance != null)
        {
            instanceAddress = instance.RemoteToken;
        }

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
            throw new NotImplementedException("Shouldn't use same hook callback for 2 patches of the same method");
        }
        // Check for duplicate hooks on same instance and position
        if (methodHooks.Any(existingHook => existingHook.Value.Position == pos && existingHook.Value.InstanceAddress == instanceAddress))
        {
            throw new NotImplementedException($"Can not set 2 hooks in the same position on the same {(instanceAddress == 0 ? "target (all instances)" : "instance")}");
        }

        methodHooks.Add(hookAction, new PositionedLocalHook(hookAction, wrappedHook, pos, instanceAddress));

        List<string> parametersTypeFullNames;
        if (methodToHook is IRttiMethodBase rttiMethod)
        {
            // Skipping 'this' parameter for instance methods
            int skipCount = methodToHook.IsStatic ? 0 : 1;
            parametersTypeFullNames =
                rttiMethod.LazyParamInfos.Skip(skipCount).Select(prm => prm.TypeResolver.TypeFullName).ToList();
        }
        else
        {
            parametersTypeFullNames =
                methodToHook.GetParameters().Select(prm => prm.ParameterType.FullName).ToList();
        }

        return _app.Communicator.HookMethod(methodToHook, pos, wrappedHook, parametersTypeFullNames, instanceAddress);
    }

    /// <summary>
    /// Hook a method on a specific instance using a dynamic object
    /// </summary>
    public bool HookMethod(MethodBase methodToHook, HarmonyPatchPosition pos, DynamifiedHookCallback hookAction, dynamic instance)
    {
        RemoteObject remoteObj = null;
        
        // Try to extract RemoteObject from dynamic
        if (instance != null)
        {
            // If it's already a RemoteObject, use it directly
            if (instance is RemoteObject ro)
            {
                remoteObj = ro;
            }
            // Otherwise, try to get the underlying RemoteObject from DynamicRemoteObject
            else
            {
                try
                {
                    // Cache the PropertyInfo for better performance (thread-safe)
                    if (_remoteObjectProperty == null)
                    {
                        lock (_remoteObjectPropertyLock)
                        {
                            if (_remoteObjectProperty == null)
                            {
                                _remoteObjectProperty = instance.GetType().GetProperty("RemoteObject", 
                                    System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                            }
                        }
                    }
                    
                    if (_remoteObjectProperty != null)
                    {
                        remoteObj = _remoteObjectProperty.GetValue(instance) as RemoteObject;
                    }
                }
                catch
                {
                    throw new ArgumentException("Unable to extract RemoteObject from the provided dynamic instance. " +
                        "Please provide a RemoteObject or DynamicRemoteObject.");
                }
            }
        }

        return HookMethod(methodToHook, pos, hookAction, remoteObj);
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
                        if (_app is UnmanagedRemoteApp ura)
                        {
                            // {[ObjectOrRemoteAddress] RemoteAddress: 0x000000d1272fb618, Type: libSpen_base.dll!SPen::File}
                            string module = oora.Assembly;
                            if (module == null)
                            {
                                int separatorPos = oora.Type.IndexOf("!");
                                if (separatorPos != -1)
                                    module = oora.Type.Substring(0, separatorPos);
                            }
                            if (module != null)
                                ura.Communicator.StartOffensiveGC(module);
                        }
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
        DynamifiedHookCallback finalizer = null,
        RemoteObject instance = null)
    {
        if (prefix == null &&
            postfix == null &&
            finalizer == null)
        {
            throw new ArgumentException("No hooks defined.");
        }

        if (prefix != null)
        {
            HookMethod(original, HarmonyPatchPosition.Prefix, prefix, instance);
        }
        if (postfix != null)
        {
            HookMethod(original, HarmonyPatchPosition.Postfix, postfix, instance);
        }
        if (finalizer != null)
        {
            HookMethod(original, HarmonyPatchPosition.Finalizer, finalizer, instance);
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