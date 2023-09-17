using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using ScubaDiver.API.Protocol.SimpleHttp;

namespace Lifeboat;

public class Program
{
    static bool HandleCommandLineArgs(string[] args, out int pid, out int diverPort)
    {
        pid = 0;
        diverPort = 0;
        string helpText = "Usage: lifeboat.exe <pid with diver> <port offset (optional)>";

        // Check for -h flag
        if (args.Contains("-h"))
        {
            Console.WriteLine(helpText);
            return false;
        }

        // Check if there are enough arguments
        if (args.Length < 1)
        {
            Console.WriteLine("Error: not enough arguments provided.");
            Console.WriteLine(helpText);
            return false;
        }

        // Attempt to parse arguments
        if (!int.TryParse(args[0], out pid))
        {
            Console.WriteLine("Error: invalid argument 'pid'.");
            Console.WriteLine(helpText);
            return false;
        }
        diverPort = pid;

        // Attempt to offset (optional)
        // Offset is optional anyway...
        if (args.Length > 1)
        {
            if (!int.TryParse(args[1], out int offset))
            {
                Console.WriteLine("Error: invalid argument 'offset'.");
                Console.WriteLine(helpText);
                return false;

            }
            diverPort += offset;
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

    static void Main(string[] args)
    {
        Console.WriteLine("                __/___            ");
        Console.WriteLine("          _____/______|           ");
        Console.WriteLine("  _______/_____\\_______\\_____   ");
        Console.WriteLine("  \\  Lifeboat    < < <       |   ");
        Console.WriteLine("~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~");

        if (!HandleCommandLineArgs(args, out int pid, out int port))
            return;

        BindToTargetProcess(pid);

        HandleConnections(port);
    }

    public static void BindToTargetProcess(int targetProcessId)
    {
        try
        {
            // Attach to the target process.
            Process targetProcess = Process.GetProcessById(targetProcessId);

            // Create a thread to monitor the target process.
            Thread monitoringThread = new Thread(() =>
            {
                // Wait for the target process to exit.
                targetProcess.WaitForExit();

                // The target process has exited, so exit the current process.
                Environment.Exit(0);
            });

            // Set the thread as a background thread so that it exits when the main thread exits.
            monitoringThread.IsBackground = true;

            // Start monitoring the target process.
            monitoringThread.Start();
        }
        catch (ArgumentException)
        {
            Console.WriteLine($"Process with PID {targetProcessId} not found.");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error binding to target process: {ex.Message}");
        }
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
