using System;
using ScubaDiver.API;
using ScubaDiver.API.Interactions.Dumps;

namespace RemoteNET;

public class RemoteCharStar : RemoteObject
{
    public override ulong RemoteToken { get; }
    public string _val;
    public ManagedRemoteApp _app;
    public override object App => _app;

    public RemoteCharStar(ManagedRemoteApp app, ulong remoteToken, string val)
    {
        _app = app;
        RemoteToken = remoteToken;
        _val = val;
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

    public override TypeDump GetTypeDump()
    {
        throw new NotImplementedException();
    }
}