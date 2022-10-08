namespace ScubaDiver.API
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