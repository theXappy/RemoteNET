namespace ScubaDiver.API.Dumps
{
    public class DiverError
    {
        public string Error { get; set; }
        public string StackTrace { get; set; }
        public DiverError()
        {
        }
        public DiverError(string err, string stackTrace)
        {
            Error = err;
            StackTrace = stackTrace;
        }
    }
}