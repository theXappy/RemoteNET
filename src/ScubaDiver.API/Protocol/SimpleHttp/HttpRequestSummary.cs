using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Linq;
using System.Text;

namespace ScubaDiver.API.Protocol.SimpleHttp
{
    public class HttpRequestSummary
    {
        public string Method { get; set; }
        public string Url { get; set; }
        public NameValueCollection QueryString { get; set; }
        public string ContentType { get; set; }
        public byte[] Body { get; set; }
        public string BodyString => Encoding.UTF8.GetString(Body);

        public string RequestId
        {
            get => QueryString.Get("requestId");
            set => QueryString["requestId"] = value;
        }

        public HttpRequestSummary()
        {
            QueryString = new NameValueCollection();
            Body = Array.Empty<byte>();
        }

        public static HttpRequestSummary FromJson(string url, NameValueCollection queryString, string json)
        {
            HttpRequestSummary result = new HttpRequestSummary()
            {
                Method = "GET",
                Url = url,
                QueryString = queryString,
            };
            if (!String.IsNullOrEmpty(json))
            {
                result.Method = "POST";
                result.ContentType = HttpResponseSummary.JsonMimeType;
                result.Body = Encoding.UTF8.GetBytes(json);
            }
            return result;
        }
        public static HttpRequestSummary FromJson(string url, Dictionary<string,string> queryStringDict, string json)
        {
            NameValueCollection queryString = new NameValueCollection();
            if (queryStringDict != null)
            {
                foreach (KeyValuePair<string, string> keyValuePair in queryStringDict)
                {
                    queryString[keyValuePair.Key] = keyValuePair.Value;
                }
            }

            return FromJson(url, queryString, json);
        }

        public override string ToString()
        {
            return $"HTTP Request. Method: {Method}, URL: {Url}, Query: {{ {(string.Join(",", QueryString.AllKeys.Select(key => $"{key}={QueryString[key]}")))} }}";
        }
    }
}