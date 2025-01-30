using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Sockets;
using System.Text;
using System.Threading;

namespace ScubaDiver.API.Protocol.SimpleHttp
{
    public class SimpleHttpProtocolParser
    {
        public static HttpRequestSummary? ReadRequest(NetworkStream networkStream, CancellationToken token) => Read<HttpRequestSummary>(networkStream, token);
        public static HttpResponseSummary? ReadResponse(NetworkStream networkStream, CancellationToken token) => Read<HttpResponseSummary>(networkStream, token);

        public static T Read<T>(NetworkStream networkStream, CancellationToken token)
        {
            object res;
            MemoryStream memoryStream = new MemoryStream();

            if (token.IsCancellationRequested)
                throw new OperationCanceledException(token);

            ReadHttpMessageFromStream(networkStream, memoryStream);
            byte[] requestData = memoryStream.ToArray();

            int numConsumed;
            if (typeof(T) == typeof(HttpRequestSummary))
            {
                numConsumed = SimpleHttpEncoder.TryParseHttpRequest(requestData, out HttpRequestSummary summary);
                res = summary;
            }
            else if (typeof(T) == typeof(HttpResponseSummary))
            {
                numConsumed = SimpleHttpEncoder.TryParseHttpResponse(requestData, out HttpResponseSummary summary);
                res = summary;
            }
            else
            {
                throw new NotSupportedException(
                    $"{nameof(SimpleHttpProtocolParser)} only supports reading {nameof(HttpRequestSummary)} or {nameof(HttpResponseSummary)}");
            }

            if (numConsumed == 0)
            {
                string request;
                try
                {
                    request = Encoding.UTF8.GetString(requestData);
                }
                catch (Exception ex)
                {
                    request = $"**Error decoding request: {ex}**";
                }

                throw new Exception($"SimpleHttpEncoder failed to parse request. Request: '{request}'");
            }

            return (T)res;
        }

        public static void ReadHttpMessageFromStream(Stream input, MemoryStream output)
        {
            int byteRead;
            StringBuilder headerBuilder = new StringBuilder();
            bool contentLengthFound = false;
            int contentLength = 0;

            while ((byteRead = input.ReadByte()) != -1)
            {
                output.WriteByte((byte)byteRead);
                headerBuilder.Append((char)byteRead);

                string header = headerBuilder.ToString();

                if (!contentLengthFound && header.Contains("Content-Length"))
                {
                    int lengthIndex = header.IndexOf("Content-Length");
                    int colonsIndex = header.IndexOf(':', lengthIndex);
                    int valueStartIndex = colonsIndex + 1;
                    int valueEndIndex = header.IndexOf('\r', valueStartIndex);

                    if (colonsIndex != -1 && valueStartIndex < valueEndIndex) // Ensure value exists
                    {
                        string lengthString = header.Substring(valueStartIndex, valueEndIndex - valueStartIndex).Trim();
                        contentLength = int.Parse(lengthString);
                        contentLengthFound = true;
                    }
                }

                if (header.EndsWith("\r\n\r\n"))
                {
                    if (contentLengthFound)
                    {
                        byte[] bodyBuffer = new byte[contentLength];
                        int bytesRead = 0;

                        while (bytesRead < contentLength)
                        {
                            int bytesReadThisTime = input.Read(bodyBuffer, bytesRead, contentLength - bytesRead);
                            if (bytesReadThisTime == 0)
                            {
                                throw new IOException("Unexpected end of stream while reading request body.");
                            }
                            bytesRead += bytesReadThisTime;

                        }

                        output.Write(bodyBuffer, 0, contentLength);
                    }

                    // Success
                    return;
                }
            }

            // Failure 
            throw new IOException("Unexpected end of stream while reading request header.");
        }

        public static void WriteRequest(NetworkStream networkStream, HttpRequestSummary summary) => Write(networkStream, summary);
        public static void WriteResponse(NetworkStream networkStream, HttpResponseSummary summary) => Write(networkStream, summary);

        public static void Write<T>(NetworkStream networkStream, T summary)
        {
            byte[] encoded;

            bool success;
            if (summary is HttpRequestSummary reqSummary)
            {
                success = SimpleHttpEncoder.TryEncodeHttpRequest(reqSummary, out encoded);
            }
            else if (summary is HttpResponseSummary respSummary)
            {
                success = SimpleHttpEncoder.TryEncodeHttpResponse(respSummary, out encoded);
            }
            else
            {
                throw new NotSupportedException(
                    $"{nameof(SimpleHttpProtocolParser)} only supports reading {nameof(HttpRequestSummary)} or {nameof(HttpResponseSummary)}");
            }

            if (!success)
            {
                throw new Exception(
                    $"SimpleHttpEncoder failed to encode our summary of type '{summary.GetType().Name}'. Summary: {summary}");
            }

            networkStream.Write(encoded, 0, encoded.Length);
        }
    }
}
