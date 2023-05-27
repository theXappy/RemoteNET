using ScubaDiver.API;
using System;

namespace RemoteNET;

public interface IRemoteObject
{
    public ulong RemoteToken { get; }

    public Type GetRemoteType();
    public dynamic Dynamify();

    public ObjectOrRemoteAddress GetItem(ObjectOrRemoteAddress key);
}