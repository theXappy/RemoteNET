using ScubaDiver.API.Hooking;

namespace RemoteNET.Common
{
    public delegate void DynamifiedHookCallback(HookContext context, dynamic instance, dynamic[] args, ref dynamic retValue);
}
