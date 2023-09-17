using System;
using ScubaDiver.API;

namespace RemoteNET;

public class RemoteCharStar : RemoteObject
{
    public override ulong RemoteToken { get; }
    private string _val;

    public RemoteCharStar(ulong remoteToken, string val)
    {
        RemoteToken = remoteToken;
        _val = val;
    }

    public override dynamic Dynamify()
    {
        return new DynamicRemoteCharStar(_val);
    }

    public override ObjectOrRemoteAddress GetItem(ObjectOrRemoteAddress key)
    {
        throw new InvalidOperationException($"Can't call GetItem on a {typeof(RemoteCharStar)}");
    }

    public override Type GetRemoteType()
    {
        return typeof(ScubaDiver.API.CharStar);
    }
}