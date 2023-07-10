using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Sockets;
using System.Net;
using System.Text;
using System.Threading.Tasks;
using ScubaDiver.API.Protocol;

namespace LifeboatProxy
{
    public class Program
    {
        public static Task Main(string[] args)
        {
            int proxyPort = 8080; // The port on which the proxy listens for incoming requests
            string targetHost = "127.0.0.1"; // The IP address or hostname of the target server
            int targetManagedPort = int.Parse(args[0]); // The port on which the target server is running
            int targetUnmanagedPort = targetManagedPort + 2;
            Console.WriteLine("My Port: " + proxyPort);
            Console.WriteLine("Target Lifeboat (Managed) Port: " + targetManagedPort);
            Console.WriteLine("Target Lifeboat (Unmanaged) Port: " + targetUnmanagedPort);

            HttpListener listener = new HttpListener();
            listener.Prefixes.Add($"http://localhost:{proxyPort}/");
            listener.Start();
            Console.WriteLine($"Proxy server is listening on port {proxyPort}...");

            while (true)
            {
                HttpListenerContext context = listener.GetContextAsync().Result;
                Console.WriteLine("New context!");
                _ = HandleContextAsync(context, targetHost, targetManagedPort, targetUnmanagedPort);
            }
        }

        static async Task HandleContextAsync(HttpListenerContext context, string targetHost, int targetManagedPort, int targetUnmanagedPort)
        {
            string requestUrl = context.Request.Url.AbsolutePath;
            Console.WriteLine($"Received request: {requestUrl}");

            int targetPort = targetManagedPort;
            if (requestUrl.StartsWith("/native/", StringComparison.OrdinalIgnoreCase))
            {
                targetPort = targetUnmanagedPort;
                requestUrl = requestUrl.Remove(0, "/native".Length);
            }

            using TcpClient targetClient = new TcpClient();
            await targetClient.ConnectAsync(targetHost, targetPort);
            using NetworkStream targetStream = targetClient.GetStream();

            OverTheWireRequest r = new OverTheWireRequest()
            {
                RequestId = 0,
                UrlAbsolutePath = requestUrl,
                QueryString = ParseQueryString(context.Request.QueryString),
                Body = ReadRequestBody(context)
            };
            RnetProtocolParser.Write(targetClient, r);
            Console.WriteLine($"Forwarded request: {requestUrl}");

            OverTheWireRequest resp = RnetProtocolParser.Parse(targetClient);
            byte[] respBody = Encoding.UTF8.GetBytes(resp.Body);
            context.Response.OutputStream.Write(respBody);
            context.Response.Close();
            Console.WriteLine($"Forwarded response: {requestUrl}, body: {respBody.Length} bytes");
        }
        static Dictionary<string, string> ParseQueryString(System.Collections.Specialized.NameValueCollection queryString)
        {
            var queryParameters = new Dictionary<string, string>();

            foreach (var key in queryString.AllKeys)
            {
                var value = queryString[key];
                queryParameters[key] = value;
            }

            return queryParameters;
        }
        static string ReadRequestBody(HttpListenerContext context)
        {
            using (var reader = new StreamReader(context.Request.InputStream, context.Request.ContentEncoding))
            {
                return reader.ReadToEnd();
            }
        }
    }
}
