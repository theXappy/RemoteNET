using System.Collections.Generic;

namespace ScubaDiver.API.Protocol
{

    public class OverTheWireRequest
    {
        public int RequestId { get; set; }
        public Dictionary<string, string> QueryString { get; set; }
        public string UrlAbsolutePath { get; set; }
        public string Body { get; set; }

    }
}