namespace ScubaDiver.API.Dumps
{
    public class DiverError
    {
        public string Error { get; set; }
        public DiverError()
        {
        }
        public DiverError(string err)
        {
            Error = err;
        }
    }
}