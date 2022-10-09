namespace ScubaDiver.API.Hooking
{
    public class HookContext
    {
        public string StackTrace { get; private set; }
        public HookContext(string stackTrace)
        {
            StackTrace = stackTrace;
        }
    }
}