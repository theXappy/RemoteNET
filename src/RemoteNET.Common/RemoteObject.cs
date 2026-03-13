using RemoteNET.Common;
using ScubaDiver.API;
using ScubaDiver.API.Hooking;
using System;
using System.Reflection;

namespace RemoteNET;

public abstract class RemoteObject
{
    public abstract ulong RemoteToken { get; }

    public abstract dynamic Dynamify();

    public abstract ObjectOrRemoteAddress GetItem(ObjectOrRemoteAddress key);
    public abstract RemoteObject Cast(Type t);
    public abstract bool Hook(MethodBase methodToHook, HarmonyPatchPosition pos, DynamifiedHookCallback hookAction);

    public abstract Type GetRemoteType();
    public new Type GetType() => GetRemoteType();
}