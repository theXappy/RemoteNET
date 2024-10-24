using ScubaDiver.API;
using System;

namespace RemoteNET;

public abstract class RemoteObject
{
    public abstract ulong RemoteToken { get; }

    public abstract dynamic Dynamify();

    public abstract ObjectOrRemoteAddress GetItem(ObjectOrRemoteAddress key);
    public abstract RemoteObject Cast(Type t);

    public abstract Type GetRemoteType();
    public new Type GetType() => GetRemoteType();
}