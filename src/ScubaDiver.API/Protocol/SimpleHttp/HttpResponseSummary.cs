using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Net;

namespace ScubaDiver.API.Protocol.SimpleHttp
{
    public class HttpResponseSummary
    {
        public HttpStatusCode StatusCode { get; set; }
        public string ContentType { get; set; }
        public Dictionary<string, string> OtherHeaders { get; set; }
        public byte[] Body { get; set; }
        public string BodyString => Encoding.UTF8.GetString(Body);

        public const string JsonMimeType = "application/json";


        public static HttpResponseSummary FromJson(HttpStatusCode statusCode, string json, Dictionary<string, string>? otherHeaders = null)
        {
            otherHeaders ??= new Dictionary<string, string>();
            HttpResponseSummary result = new HttpResponseSummary()
            {
                StatusCode = statusCode,
                Body = Array.Empty<byte>(),
                OtherHeaders = otherHeaders
            };
            if (!String.IsNullOrEmpty(json))
            {
                result.ContentType = HttpResponseSummary.JsonMimeType;
                result.Body = Encoding.UTF8.GetBytes(json);
            }
            return result;
        }

        public override string ToString()
        {
            return $"[Status = {StatusCode} ({(int)(StatusCode)})] Body = {(Body?.Any()==true ? BodyString : "EMPTY")}";
        }
    }
}