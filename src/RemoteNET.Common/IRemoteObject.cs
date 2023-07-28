using ScubaDiver.API;
using System;

namespace RemoteNET;

public interface IRemoteObject
{
    public ulong RemoteToken { get; }

    public dynamic Dynamify();

    public ObjectOrRemoteAddress GetItem(ObjectOrRemoteAddress key);

    public Type GetType() => GetRemoteType();
    public Type GetRemoteType();
}