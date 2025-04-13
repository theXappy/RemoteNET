using ScubaDiver.API;
using ScubaDiver.API.Interactions.Dumps;
using System;

namespace RemoteNET;

public abstract class RemoteObject
{
    public abstract ulong RemoteToken { get; }
    public abstract object App { get; }
    public abstract TypeDump GetTypeDump();

    public abstract ObjectOrRemoteAddress GetItem(ObjectOrRemoteAddress key);
    public abstract RemoteObject Cast(Type t);

    public abstract Type GetRemoteType();
    public new Type GetType() => GetRemoteType();
}