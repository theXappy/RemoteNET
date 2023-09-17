using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using ScubaDiver.API.Protocol.SimpleHttp;

namespace Lifeboat;

public class Program
{
    static bool HandleCommandLineArgs(string[] args, out int diverPort)
    {
        diverPort = 0;

        // Check for -h flag
        if (args.Contains("-h"))
        {
            Console.WriteLine("Usage: program.exe <diverPort>");
            return false;
        }

        // Check if there are enough arguments
        if (args.Length < 1)
        {
            Console.WriteLine("Error: not enough arguments provided.");
            Console.WriteLine("Usage: program.exe <diverPort>");
            return false;
        }

        // Attempt to parse arguments
        if (!int.TryParse(args[0], out diverPort))
        {
            Console.WriteLine("Error: invalid argument(s).");
            Console.WriteLine("Usage: program.exe <diverPort>");
            return false;
        }

        return true;
    }

    static void SendRejectRole(TcpClient client, string requestId, string error)
    {
        Dictionary<string, string> dict = new Dictionary<string, string>();
        dict["requestId"] = requestId;
        HttpResponseSummary introReject = HttpResponseSummary.FromJson(HttpStatusCode.Unauthorized, $"{{\"status\":\"reject, {error}\"}}", dict);
        SimpleHttpProtocolParser.Write(client, introReject);
        client.GetStream().Flush();
    }
    static void SendAcceptRole(TcpClient client, string requestId)
    {
        Dictionary<string, string> dict = new Dictionary<string, string>();
        dict["requestId"] = requestId;
        HttpResponseSummary introAccept = HttpResponseSummary.FromJson(HttpStatusCode.OK, "{\"status\":\"OK\"}", dict);
        SimpleHttpProtocolParser.Write(client, introAccept);
        client.GetStream().Flush();
    }

    static async Task Main(string[] args)
    {
        Console.WriteLine("                __/___            ");
        Console.WriteLine("          _____/______|           ");
        Console.WriteLine("  _______/_____\\_______\\_____   ");
        Console.WriteLine("  \\  Lifeboat    < < <       |   ");
        Console.WriteLine("~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~");

        if (!HandleCommandLineArgs(args, out int port))
            return;

        HandleConnections(port);
    }

    public static void HandleConnections(int port)
    {
        int stage = 0;
        void Log(string msg)
        {
            Console.WriteLine($"[{port}][{stage}] " + msg);
        }

        Log("Waiting for diver...");
        TcpListener listener = new TcpListener(IPAddress.Any, port);
        listener.Start();

        while (true)
        {
            // Wait for the first client to connect
            Log($"Waiting for diver...");
            TcpClient diverConnection = listener.AcceptTcpClient();
            Log($"Diver Suspect: {diverConnection.Client.RemoteEndPoint}");
            HttpRequestSummary msg = SimpleHttpProtocolParser.ReadRequest(diverConnection);
            string id = msg.QueryString.Get("requestId") ?? "999";
            if (msg == null)
            {
                Log($"NOT a Diver: Connection close prematurely");
                diverConnection.Close();
                continue;
            }
            if (msg.Url != "/proxy_intro" || msg.BodyString != "{\"role\":\"diver\"}")
            {
                Log($"NOT a Diver: {diverConnection.Client.RemoteEndPoint}");
                SendRejectRole(diverConnection, id, "No diver");
                diverConnection.Close();
                continue;
            }

            SendAcceptRole(diverConnection, id);
            stage++;
            var DiverCon = new DiverConnection(diverConnection);
            Log($" ~~~~> Diver Found: {diverConnection.Client.RemoteEndPoint} <~~~~~");

            // Start accepting more clients
            List<ClientConnection> clients = new List<ClientConnection>();
            while (true)
            {
                Log("Waiting for connection...");
                TcpClient clientConnection = listener.AcceptTcpClient();
                Log("New client found! Linking to diver.");
                var clientCon = new ClientConnection(clientConnection, DiverCon);

                // A good time to clean up dead clients
                Log($"Cleaning up clients list. Count Before: {clients.Count}");
                clients = clients.Where(c => c.Alive).ToList();
                Log($"Cleaning up clients list. Count After: {clients.Count}");

                // Store current client so GC doesn't kill it
                clients.Add(clientCon);
                Log($"Added new client to list. New clients count: {clients.Count}");
            }
        }
    }


}
