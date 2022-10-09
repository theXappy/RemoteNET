namespace ScubaDiver.API.Hooking
{

    /// Used by RmoteHarmony and Communicator
    public delegate void LocalHookCallback(HookContext context, ObjectOrRemoteAddress instance, ObjectOrRemoteAddress[] args);
}