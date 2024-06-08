using ScubaDiver.API.Hooking;

namespace RemoteNET.Common
{
    public delegate void HookAction(HookContext context, dynamic instance, dynamic[] args, dynamic retValue);
}
