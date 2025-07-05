namespace ScubaDiver.API.Hooking
{

    /// Used by RemoteHarmony and Communicator
    public delegate void LocalHookCallback(HookContext context, ObjectOrRemoteAddress instance, ObjectOrRemoteAddress[] args, ref ObjectOrRemoteAddress retValue);
}