using System;
using System.Collections.Generic;
using System.Reflection;
using RemoteNET.Common;
using RemoteNET.Internal;
using ScubaDiver.API;
using ScubaDiver.API.Hooking;
using ScubaDiver.API.Interactions;
using ScubaDiver.API.Interactions.Dumps;

namespace RemoteNET;

public class UnmanagedRemoteObject : RemoteObject
{
    private static int NextIndex = 1;
    public int Index;

    private readonly RemoteApp _app;
    private RemoteObjectRef _ref;
    private Type _type = null;

    private readonly Dictionary<Delegate, DiverCommunicator.LocalEventCallback> _eventCallbacksAndProxies;

    public override ulong RemoteToken => _ref.Token;

    internal UnmanagedRemoteObject(RemoteObjectRef reference, RemoteApp remoteApp)
    {
        Index = NextIndex++;
        _app = remoteApp;
        _ref = reference;
        _eventCallbacksAndProxies = new Dictionary<Delegate, DiverCommunicator.LocalEventCallback>();
    }

    public override Type GetRemoteType()
    {
        return _type ??= _app.GetRemoteType(_ref.GetTypeDump());
    }

    public override dynamic Dynamify()
    {
        // Adding fields 
        TypeDump typeDump = _ref.GetTypeDump();

        var factory = new DynamicRemoteObjectFactory();
        return factory.Create(_app, this, typeDump);
    }

    public override ObjectOrRemoteAddress GetItem(ObjectOrRemoteAddress key)
    {
        throw new NotImplementedException();
    }

    public (bool hasResults, ObjectOrRemoteAddress returnedValue) InvokeMethod(string methodName, params ObjectOrRemoteAddress[] args)
    {
        InvocationResults invokeRes = _ref.InvokeMethod(methodName, Array.Empty<string>(), args);
        if (invokeRes.VoidReturnType)
        {
            return (false, null);
        }
        return (true, invokeRes.ReturnedObjectOrAddress);
    }

    public override string ToString()
    {
        return $"{nameof(UnmanagedRemoteObject)}. Type: {_type?.FullName ?? "UNK"} Reference: [{_ref}]";
    }

    public override RemoteObject Cast(Type t)
    {
        TypeDump dumpType = _app.Communicator.DumpType(t.FullName, t.Assembly.GetName().Name);

        RemoteObjectRef ror = new RemoteObjectRef(_ref.RemoteObjectInfo, dumpType, _ref.CreatingCommunicator);
        return new UnmanagedRemoteObject(ror, _app);
    }

    /// <summary>
    /// Hooks a method on this specific instance.
    /// This is a convenience method that calls app.HookingManager.HookMethod with this instance.
    /// </summary>
    /// <param name="methodToHook">The method to hook</param>
    /// <param name="pos">Position of the hook (Prefix, Postfix, or Finalizer)</param>
    /// <param name="hookAction">The callback to invoke when the method is called</param>
    /// <returns>True on success</returns>
    public override bool Hook(MethodBase methodToHook, HarmonyPatchPosition pos, DynamifiedHookCallback hookAction)
    {
        return _app.HookingManager.HookMethod(methodToHook, pos, hookAction, this);
    }
}