namespace ScubaDiver.API.Hooking
{
    public class HookContext
    {
        public string StackTrace { get; private set; }
        public int ThreadId { get; private set; }
        public bool skipOriginal { get; set; }
        public HookContext(string stackTrace, int threadId)
        {
            StackTrace = stackTrace;
            ThreadId = threadId;
            skipOriginal = false;
        }
    }
}