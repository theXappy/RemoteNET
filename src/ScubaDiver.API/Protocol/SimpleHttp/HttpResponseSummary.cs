using System;
using System.Text;
using System.Net;

namespace ScubaDiver.API.Protocol.SimpleHttp
{
    public class HttpResponseSummary
    {
        public HttpStatusCode StatusCode { get; set; }
        public string ContentType { get; set; }
        public byte[] Body { get; set; }
        public string BodyString => Encoding.UTF8.GetString(Body);

        public const string JsonMimeType = "application/json";


        public static HttpResponseSummary FromJson(HttpStatusCode statusCode, string json)
        {
            HttpResponseSummary result = new HttpResponseSummary()
            {
                StatusCode = statusCode,
                Body = Array.Empty<byte>()
            };
            if (!String.IsNullOrEmpty(json))
            {
                result.ContentType = HttpResponseSummary.JsonMimeType;
                result.Body = Encoding.UTF8.GetBytes(json);
            }
            return result;
        }
    }
}