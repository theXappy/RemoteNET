using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Threading.Channels;
using Newtonsoft.Json.Linq;
using ScubaDiver.API.Protocol;
using ScubaDiver.API.Protocol.SimpleHttp;

namespace Lifeboat;

public class Program
{
    private static EndPoint _diverBootstrap = null;

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
            TcpClient diverBoostrapConnection = listener.AcceptTcpClient();
            Log($"Diver Bootstrap Suspect: {diverBoostrapConnection.Client.RemoteEndPoint}");
            HttpRequestSummary msg = SimpleHttpProtocolParser.ReadRequest(diverBoostrapConnection);
            string id = msg.QueryString.Get("requestId") ?? "999";
            if (msg == null)
            {
                Log($"NOT a Diver: Connection close prematurely");
                diverBoostrapConnection.Close();
                continue;
            }
            if (msg.Url != "/proxy_intro" || msg.BodyString != "{\"role\":\"diver_bootstrap\"}")
            {
                Log($"NOT a Diver: {diverBoostrapConnection.Client.RemoteEndPoint}");
                SendRejectRole(diverBoostrapConnection, id, "No diver");
                diverBoostrapConnection.Close();
                continue;
            }

            SendAcceptRole(diverBoostrapConnection, id);
            stage++;
            _diverBootstrap = diverBoostrapConnection.Client.RemoteEndPoint;
            Log($" ~~~~> Diver Bootstrap Found: {diverBoostrapConnection.Client.RemoteEndPoint} <~~~~~");

            // Start accepting more clients
            while (true)
            {
                Log("Waiting for connection...");
                TcpClient clientConnection = listener.AcceptTcpClient();
                var endpoint = clientConnection.Client.RemoteEndPoint as IPEndPoint;
                Log($"New connection from: {endpoint}");

                // Check for spawned diver...
                if (Equals(endpoint.Address, IPAddress.Parse("127.0.0.1")) &&
                    _portsToActions.TryGetValue(endpoint.Port, out Action<TcpClient> callback))
                {
                    // This is a spawned diver connection
                    Log($"New spawned diver! {clientConnection.Client.RemoteEndPoint}");
                    callback(clientConnection);
                }
                else
                {
                    // Normal client connection
                    Log($"New client! {clientConnection.Client.RemoteEndPoint}");
                    Task.Run(() => PairNewClient(diverBoostrapConnection, clientConnection));
                }
            }
        }
    }

    private static void PairNewClient(TcpClient diverBoostrapConnection, TcpClient clientConnection)
    {
        Console.WriteLine($"Spawning client for {clientConnection.Client.RemoteEndPoint} ...");
        // Client is on hold until we can spawn a new connection from the Diver
        TcpClient diverConnection = ConnectToDiver(diverBoostrapConnection);

        void Log(string msg)
        {
            Console.WriteLine($"[Diver:{diverConnection.Client.RemoteEndPoint}][Client:{clientConnection.Client.RemoteEndPoint}] " + msg);
        }
        Log($"Diver suspect spawned: {diverConnection.Client.RemoteEndPoint}");
        HttpRequestSummary msg = SimpleHttpProtocolParser.ReadRequest(diverConnection);
        string id = msg.QueryString.Get("requestId") ?? "999";
        if (msg == null)
        {
            Log($"NOT a Diver: Connection close prematurely");
            diverBoostrapConnection.Close();
            return;
        }
        if (msg.Url != "/proxy_intro" || msg.BodyString != "{\"role\":\"diver\"}")
        {
            Log($"NOT a Diver: {diverConnection.Client.RemoteEndPoint}");
            SendRejectRole(diverConnection, id, "No diver");
            diverConnection.Close();
            return;
        }
        Log($"Diver spawned confirmed! {diverConnection.Client.RemoteEndPoint}");
        SendAcceptRole(diverConnection, id);


        // Start the data transfer on the diver connection
        var diverIn = new BlockingCollection<HttpRequestSummary>();
        var diverOut = new BlockingCollection<HttpResponseSummary>();
        var diverReaderTask = Task.Run(() => Reader(diverConnection, diverOut));
        var diverWriterTask = Task.Run(() => Writer(diverConnection, diverIn));
        diverReaderTask.ContinueWith(t =>
        {
            Log("DIVER READER DIED :(");
        });
        diverWriterTask.ContinueWith(t =>
        {
            Log("DIVER WRITER DIED :(");
        });

        // Start the data transfer on the client connection
        var clientIn = diverOut;
        var clientOut = diverIn;

        CancellationTokenSource src = new CancellationTokenSource();
        var t1 = Task.Run(() => Reader(clientConnection, clientOut, src.Token)).ContinueWith(t => Log("Consumer Reader Died."));
        var t2 = Task.Run(() => Writer(clientConnection, clientIn, src.Token)).ContinueWith(t => Log("Consumer Reader Died."));

        Log("Waiting for client to disconnect...");
        try
        {
            Task.WaitAny(t1, t2);
        }
        catch (Exception e)
        {
        }
        Log("Client half-disconnected!");
        src.Cancel();
        try
        {
            Task.WaitAll(t1, t2);
        }
        catch (Exception e)
        {
        }
        Log("Client disconnected!");

        try
        {
            clientConnection.Close();
        }
        catch
        {
        }
    }

    private static Dictionary<int, Action<TcpClient>> _portsToActions = new Dictionary<int, Action<TcpClient>>();

    private static TcpClient ConnectToDiver(TcpClient diverBoostrapConnection)
    {
        Console.WriteLine($"[ConnectToDiver] In");
        AutoResetEvent are = new AutoResetEvent(false);
        TcpClient diverConnection = null;

        // Find a free port
        int port = GetFreePort();
        while (_portsToActions.ContainsKey(port))
        {
            port = GetFreePort();
        }
        // Claim the port
        _portsToActions.Add(port, (con) =>
        {
            diverConnection = con;
            are.Set();
        });

        HttpRequestSummary spawnRequest = HttpRequestSummary.FromJson("spawn_new_connection", new Dictionary<string, string>()
        {
            ["port"] = port.ToString(),
        }, string.Empty);

        Console.WriteLine($"Asking diver to connect from {port} ...");
        SimpleHttpProtocolParser.Write(diverBoostrapConnection, spawnRequest);

        Console.WriteLine($"Waiting on ARE...");
        are.WaitOne();
        Console.WriteLine($"ARE Ended!!!");

        // Clean up
        _portsToActions.Remove(port);
        return diverConnection;
    }

    private static int GetFreePort()
    {
        int port = 0;
        TcpListener listener = null;

        try
        {
            // Create a TCP listener on a random port
            listener = new TcpListener(IPAddress.Parse("127.0.0.1"), 0);
            listener.Start();

            // Get the assigned port
            port = ((IPEndPoint)listener.LocalEndpoint).Port;
        }
        finally
        {
            // Clean up resources
            listener?.Stop();
        }

        return port;
    }

    public static void Reader<T>(TcpClient client, BlockingCollection<T> outQueue, CancellationToken token = default)
    {
        Log(nameof(Reader), client, $"Reader Started. Type: {typeof(T).Name}");
        if (token == default)
            token = CancellationToken.None;

        while (client.Connected && !token.IsCancellationRequested)
        {
            T item = SimpleHttpProtocolParser.Read<T>(client);
            if (item != null)
            {
                outQueue.Add(item, token);
            }
            else
            {
                // Client disconnected
                break;
            }
        }
        Log(nameof(Reader), client, $"TransferData == OUT ?!?!?!?!?! ==");
    }

    public static void Writer<T>(TcpClient client, BlockingCollection<T> inQueue, CancellationToken token = default)
    {
        Log(nameof(Writer), client, $"Writer Started. Type: {typeof(T).Name}");
        if (token == default)
            token = CancellationToken.None;

        while (client.Connected && !token.IsCancellationRequested)
        {
            T toSend = inQueue.Take(token);

            SimpleHttpProtocolParser.Write(client, toSend);
        }
        Log(nameof(Writer), client, $"Writer == OUT ?!?!?!?!?! ==");
    }

    private static void Log(string method, TcpClient client, string msg)
    {
        bool isDiver = client.Client.RemoteEndPoint == _diverBootstrap;
        string isDiverStr = isDiver ? "[ *DIVER* ]" : "";
        Console.WriteLine($"[{client?.Client?.RemoteEndPoint}]{isDiverStr}[{method}] {msg}");
    }

}
