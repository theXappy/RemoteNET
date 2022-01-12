namespace ScubaDiver.API
{
    /// Used by RmoteHarmony and Communicator
    public delegate void LocalHookCallback(ObjectOrRemoteAddress instance, ObjectOrRemoteAddress[] args);
}