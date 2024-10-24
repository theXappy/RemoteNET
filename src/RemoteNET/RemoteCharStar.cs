using System;
using ScubaDiver.API;

namespace RemoteNET;

public class RemoteCharStar : RemoteObject
{
    public override ulong RemoteToken { get; }
    private string _val;
    private ManagedRemoteApp _app;

    public RemoteCharStar(ManagedRemoteApp app, ulong remoteToken, string val)
    {
        _app = app;
        RemoteToken = remoteToken;
        _val = val;
    }

    public override dynamic Dynamify()
    {
        return new DynamicRemoteCharStar(_app, RemoteToken, _val);
    }

    public override ObjectOrRemoteAddress GetItem(ObjectOrRemoteAddress key)
    {
        throw new InvalidOperationException($"Can't call GetItem on a {typeof(RemoteCharStar)}");
    }

    public override Type GetRemoteType()
    {
        return typeof(ScubaDiver.API.CharStar);
    }

    public override RemoteObject Cast(Type t)
    {
        throw new NotImplementedException("Not implemented for char* remote objects");
    }
}