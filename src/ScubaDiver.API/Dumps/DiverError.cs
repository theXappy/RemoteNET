namespace ScubaDiver.API.Dumps
{
    public class DiverError
    {
        string Error { get; set; }
        public DiverError(string err)
        {
            Error = err;
        }
    }
}