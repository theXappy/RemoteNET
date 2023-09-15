using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Net;
using System.Net.Mime;
using System.Reflection.Metadata;
using System.Text;
using System.Web;

namespace ScubaDiver.API.Protocol.SimpleHttp
{
    public static class SimpleHttpEncoder
    {
        /// <returns>Bytes consumed. 0 if failed to parse, positive if parsed.</returns>
        public static int TryParseHttpRequest(byte[] rawData, out HttpRequestSummary summary)
        {
            summary = null;

            try
            {
                string request = Encoding.UTF8.GetString(rawData);
                int firstLineEnd = request.IndexOf("\r\n");
                if (firstLineEnd == -1)
                {
                    return 0;
                }

                string firstLine = request.Substring(0, firstLineEnd);
                string[] parts = firstLine.Split(' ');

                if (parts.Length < 3)
                {
                    return 0;
                }

                string method = parts[0];
                string urlAndQuery = parts[1];

                int urlEnd = urlAndQuery.IndexOf('?');
                if (urlEnd == -1)
                {
                    urlEnd = urlAndQuery.Length;
                }

                string url = urlAndQuery.Substring(0, urlEnd);
                NameValueCollection queryString = HttpUtility.ParseQueryString(new Uri("http://fake.com/"+urlAndQuery, UriKind.Absolute).Query);

                int headersEnd = request.IndexOf("\r\n\r\n", firstLineEnd);
                if (headersEnd == -1)
                {
                    return 0;
                }

                // Find Content-Length header
                int contentLengthIndex = request.IndexOf("Content-Length:", firstLineEnd, headersEnd - firstLineEnd, StringComparison.OrdinalIgnoreCase);
                int contentLength = 0;
                if (contentLengthIndex != -1)
                {
                    int valueStart = contentLengthIndex + "Content-Length:".Length;
                    int valueEnd = request.IndexOf('\r', valueStart, (headersEnd+1) - valueStart);
                    if (valueEnd == -1 || !int.TryParse(request.Substring(valueStart, valueEnd - valueStart).Trim(),
                            out contentLength))
                    {
                        return 0;
                    }
                }

                int bodyStart = headersEnd + 4; // Skip "\r\n"

                byte[] body = Array.Empty<byte>();
                if (contentLength > 0 && bodyStart + contentLength <= rawData.Length)
                {
                    body = new byte[contentLength];
                    Array.Copy(rawData, bodyStart, body, 0, contentLength);
                }

                summary = new HttpRequestSummary
                {
                    Method = method,
                    Url = url,
                    QueryString = queryString,
                    Body = body
                };

                return bodyStart + contentLength;
            }
            catch
            {
                return 0;
            }
        }

        public static bool TryEncodeHttpRequest(HttpRequestSummary summary, out byte[] requestBytes)
        {
            try
            {
                StringBuilder requestBuilder = new StringBuilder();
                string urlAndQuery = BuildUrlWithQueryString(summary.Url, summary.QueryString);
                string header = $"{summary.Method} {urlAndQuery} HTTP/1.1\r\n";
                requestBuilder.Append(header);

                if (summary.Body != null && summary.Body.Length > 0)
                {
                    requestBuilder.Append($"Content-Type: {summary.ContentType}\r\n");
                    requestBuilder.Append($"Content-Length: {summary.Body.Length}\r\n");
                }
                requestBuilder.Append("\r\n");
                string request = requestBuilder.ToString();


                byte[] body = summary.Body ?? Array.Empty<byte>();
                requestBytes = new byte[Encoding.UTF8.GetByteCount(request) + body.Length];

                Encoding.UTF8.GetBytes(request, 0, request.Length, requestBytes, 0);
                Array.Copy(body, 0, requestBytes, request.Length, body.Length);

                return true;
            }
            catch
            {
                requestBytes = null;
                return false;
            }
        }

        static string BuildUrlWithQueryString(string baseUrl, NameValueCollection parameters)
        {           // Encode the parameters and build the query string
            string queryString = "";
            for (int i = 0; i < parameters.Count; i++)
            {
                string key = HttpUtility.UrlEncode(parameters.Keys[i]);
                string value = HttpUtility.UrlEncode(parameters[i]);
                queryString += $"{key}={value}";
                if (i < parameters.Count - 1)
                {
                    queryString += "&";
                }
            }

            // Add the '?' at the beginning
            queryString = "?" + queryString;

            if (!baseUrl.StartsWith("/"))
                baseUrl = "/" + baseUrl;

            string url = baseUrl + queryString;
            return url;
        }

        public static bool TryEncodeHttpResponse(HttpResponseSummary summary, out byte[] responseBytes)
        {
            try
            {
                StringBuilder responseBuilder = new StringBuilder();
                responseBuilder.Append($"HTTP/1.1 {((int)summary.StatusCode)} {summary.StatusCode}\r\n");
                responseBuilder.Append("Connection: close\r\n"); // Trying to cause Web browsers to release the connection.
                foreach (var header in summary.OtherHeaders)
                {
                    responseBuilder.Append($"{header.Key}: {header.Value}\r\n"); // Trying to cause Web browsers to release the connection.
                }

                if (summary.Body != null && summary.Body.Length > 0)
                {
                    responseBuilder.Append($"Content-Type: {summary.ContentType}\r\n");
                    responseBuilder.Append($"Content-Length: {summary.Body.Length}\r\n");
                }

                responseBuilder.Append("\r\n");

                string responseHeaders = responseBuilder.ToString();
                byte[] headersBytes = Encoding.UTF8.GetBytes(responseHeaders);

                if (summary.Body != null && summary.Body.Length > 0)
                {
                    responseBytes = new byte[headersBytes.Length + summary.Body.Length];
                    Array.Copy(headersBytes, responseBytes, headersBytes.Length);
                    Array.Copy(summary.Body, 0, responseBytes, headersBytes.Length, summary.Body.Length);
                }
                else
                {
                    responseBytes = headersBytes;
                }

                return true;
            }
            catch
            {
                responseBytes = null;
                return false;
            }
        }

        /// <returns>Bytes consumed. 0 if failed to parse, positive if parsed.</returns>
        public static int TryParseHttpResponse(byte[] rawData, out HttpResponseSummary summary)
        {
            summary = null;

            try
            {
                string response = Encoding.UTF8.GetString(rawData);
                int firstLineEnd = response.IndexOf("\r\n");
                if (firstLineEnd == -1)
                {
                    return 0;
                }

                string firstLine = response.Substring(0, firstLineEnd);
                string[] parts = firstLine.Split(' ');

                if (parts.Length < 3)
                {
                    return 0;
                }

                int statusCode;
                if (!int.TryParse(parts[1], out statusCode))
                {
                    return 0;
                }

                int headersEnd = response.IndexOf("\r\n\r\n", firstLineEnd);
                if (headersEnd == -1)
                {
                    return 0;
                }
                
                // Extract headers and find Content-Type
                string headers = response.Substring(firstLineEnd + 2, headersEnd - (firstLineEnd + 2));
                string[] headerLines = headers.Split(new[] { "\r\n" }, StringSplitOptions.RemoveEmptyEntries);
                string contentType = "text/plain"; // Default content type

                Dictionary<string, string> otherHeaders = new Dictionary<string, string>();
                foreach (var headerLine in headerLines)
                {
                    string[] headerParts = headerLine.Split(':');
                    if (headerParts.Length == 2)
                    {
                        if (headerParts[0].Trim().ToLower() == "content-type")
                        {
                            contentType = headerParts[1].Trim();
                            break;
                        }
                        else
                        {
                            otherHeaders[headerParts[0]] = headerParts[1].Trim();
                        }
                    }
                }

                // Find Content-Length header
                int contentLengthIndex = response.IndexOf("Content-Length:", firstLineEnd, headersEnd - firstLineEnd, StringComparison.OrdinalIgnoreCase);
                int contentLength = -1;
                if (contentLengthIndex != -1)
                {
                    int valueStart = contentLengthIndex + "Content-Length:".Length;
                    int valueEnd = response.IndexOf('\r', valueStart, (headersEnd+1) - valueStart);
                    if (valueEnd == -1 || !int.TryParse(response.Substring(valueStart, valueEnd - valueStart).Trim(),
                            out contentLength))
                    {
                        return 0;
                    }
                }

                int bodyStart = headersEnd + 4; // Skip "\r\n\r\n"

                byte[] body = Array.Empty<byte>();
                if (contentLength >= 0 && bodyStart + contentLength <= rawData.Length)
                {
                    body = new byte[contentLength];
                    Array.Copy(rawData, bodyStart, body, 0, contentLength);
                }

                summary = new HttpResponseSummary
                {
                    StatusCode = (HttpStatusCode)statusCode,
                    ContentType = contentType,
                    Body = body,
                    OtherHeaders = otherHeaders
                };

                return (bodyStart + 1) + contentLength;
            }
            catch
            {
                return 0;
            }
        }

    }
}
