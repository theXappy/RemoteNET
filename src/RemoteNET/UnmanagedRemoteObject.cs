using System;
using System.Collections.Generic;
using RemoteNET.Internal;
using ScubaDiver.API;
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
}