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
    private static EndPoint _diver = null;

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

    static void SendRejectRole(TcpClient client, string error)
    {
        HttpResponseSummary introReject = HttpResponseSummary.FromJson(HttpStatusCode.Unauthorized, $"{{\"status\":\"reject, {error}\"}}");
        SimpleHttpProtocolParser.Write(client, introReject);
        client.GetStream().Flush();
    }
    static void SendAcceptRole(TcpClient client)
    {
        HttpResponseSummary introAccept = HttpResponseSummary.FromJson(HttpStatusCode.OK, "{\"status\":\"OK\"}");
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
            if (msg == null)
            {
                Log($"NOT a Diver: Connection close prematurely");
                diverConnection.Close();
                continue;
            }
            if (msg.Url != "/proxy_intro" || msg.BodyString != "{\"role\":\"diver\"}")
            {
                Log($"NOT a Diver: {diverConnection.Client.RemoteEndPoint}");
                SendRejectRole(diverConnection, "No diver");
                diverConnection.Close();
                continue;
            }

            SendAcceptRole(diverConnection);
            stage++;
            _diver = diverConnection.Client.RemoteEndPoint;
            Log($" ~~~~> Diver Found: {diverConnection.Client.RemoteEndPoint} <~~~~~");

            // Start the data transfer on the diver connection
            var diverIn = new BlockingCollection<HttpRequestSummary>();
            var diverOut = new BlockingCollection<HttpResponseSummary>();
            var diverReaderTask = Task.Run(() => Reader(diverConnection, diverOut));
            var diverWriterTask = Task.Run(() => Writer(diverConnection, diverIn));
            diverReaderTask.ContinueWith(t =>
            {
                Log("DIVER READER DIED :(");
                Environment.Exit(10);
            });
            diverWriterTask.ContinueWith(t =>
            {
                Log("DIVER WRITER DIED :(");
                Environment.Exit(10);
            });

            // Start accepting more clients
            while (true)
            {
                Log("Waiting for client...");
                TcpClient clientConnection = listener.AcceptTcpClient();
                Log($"New client! {clientConnection.Client.RemoteEndPoint}");

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
        }
    }

    public static void Reader<T>(TcpClient client, BlockingCollection<T> outQueue, CancellationToken token = default)
    {
        Log(nameof(Reader), client, $"Reader Started. Type: {typeof(T).Name}");
        if (token == default)
            token = CancellationToken.None;

        Log(nameof(Reader), client, $" TransferData == IN ==");
        while (client.Connected && !token.IsCancellationRequested)
        {
            Log(nameof(Reader), client, $"waiting for more...");
            T item = SimpleHttpProtocolParser.Read<T>(client);
            Log(nameof(Reader), client, $"New RECV message!");
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

        Log(nameof(Writer), client, $"TransferData == IN ==");
        while (client.Connected && !token.IsCancellationRequested)
        {
            T toSend = inQueue.Take(token);
            Log(nameof(Writer), client, $"New TO_SEND message!");

            SimpleHttpProtocolParser.Write(client, toSend);
            Log(nameof(Writer), client, $"Written!");
        }
        Log(nameof(Writer), client, $"TransferData == OUT ?!?!?!?!?! ==");
    }

    private static void Log(string method, TcpClient client, string msg)
    {
        bool isDiver = client.Client.RemoteEndPoint == _diver;
        string isDiverStr = isDiver ? "[ *DIVER* ]" : "";
        Console.WriteLine($"[{client?.Client?.RemoteEndPoint}]{isDiverStr}[{method}] {msg}");
    }

}
