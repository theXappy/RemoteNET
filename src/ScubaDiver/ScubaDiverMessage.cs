using System;
using System.Collections.Generic;

namespace ScubaDiver;

public class ScubaDiverMessage
{
    public Dictionary<string, string> QueryString { get; set; }
    public string UrlAbsolutePath { get; set; }
    public string Body { get; set; }
    public Action<string> ResponseSender { get; set; }

    public ScubaDiverMessage(Dictionary<string, string> queryString, string urlAbsolutePath, string body, Action<string> responseSender)
    {
        QueryString = queryString;
        UrlAbsolutePath = urlAbsolutePath;
        Body = body;
        ResponseSender = responseSender;
    }
}