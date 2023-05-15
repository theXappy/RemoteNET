using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Linq;

namespace ScubaDiver;

public class ScubaDiverMessage
{
    public NameValueCollection QueryString { get; set; }
    public string UrlAbsolutePath { get; set; }
    public string Body { get; set; }
    public Action<string> ResponseSender { get; set; }

    public ScubaDiverMessage(Dictionary<string, string> queryString, string urlAbsolutePath, string body, Action<string> responseSender)
    {
        QueryString = new NameValueCollection();
        foreach (var kvp in queryString)
        {
            QueryString.Add(kvp.Key, kvp.Value);
        }
        UrlAbsolutePath = urlAbsolutePath;
        if (urlAbsolutePath.FirstOrDefault() != '/')
            UrlAbsolutePath = '/' + UrlAbsolutePath;
        Body = body;
        ResponseSender = responseSender;
    }
}