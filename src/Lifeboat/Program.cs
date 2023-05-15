using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Threading.Channels;
using Newtonsoft.Json.Linq;
using ScubaDiver.API.Protocol;

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

    static void SendRejectRole(TcpClient client, int requestId, string error)
    {
        OverTheWireRequest introAccept = new OverTheWireRequest
        {
            UrlAbsolutePath = "/proxy_intro",
            RequestId = requestId,
            Body = $"{{\"status\":\"reject, {error}\"}}"
        };
        RnetProtocolParser.Write(client, introAccept);
        client.GetStream().Flush();
    }
    static void SendAcceptRole(int requestId, TcpClient client)
    {
        OverTheWireRequest introAccept = new OverTheWireRequest
        {
            UrlAbsolutePath = "/proxy_intro",
            RequestId = requestId,
            Body = "{\"status\":\"OK\"}"
        };
        RnetProtocolParser.Write(client, introAccept);
        client.GetStream().Flush();
    }

    static async Task Main(string[] args)
    {
        Console.WriteLine("                __/___            ");
        Console.WriteLine("          _____/______|           ");
        Console.WriteLine("  _______/_____\\_______\\_____   ");
        Console.WriteLine("  \\  Lifeboat    < < <       |   ");
        Console.WriteLine("~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~");

        OverTheWireRequest msg;

        if (!HandleCommandLineArgs(args, out int port))
            return;

        DoTheThing(port);
    }




    public static void DoTheThing(int port)
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
            Log($"DIVER SUSPECT: {diverConnection.Client.RemoteEndPoint} >>>>>");
            var msg = RnetProtocolParser.Parse(diverConnection);
            if (msg.UrlAbsolutePath != "/proxy_intro" || msg.Body != "{\"role\":\"diver\"}")
            {
                Log($"NOT A DIVER: {diverConnection.Client.RemoteEndPoint} >>>>>>");
                SendRejectRole(diverConnection, msg.RequestId, "No diver");
                diverConnection.Close();
                continue;
            }

            SendAcceptRole(msg.RequestId, diverConnection);
            stage++;
            Log($"FOUND DIVER: {diverConnection.Client.RemoteEndPoint} >>>>>");

            // Start the data transfer on the diver connection
            var diverIn = new BlockingCollection<OverTheWireRequest>();
            var diverOut = new BlockingCollection<OverTheWireRequest>();
            var diverReaderTask = Task.Run(() => Reader(diverConnection, diverOut));
            var diverWriterTask = Task.Run(() => Writer(diverConnection, diverIn));
            diverReaderTask.ContinueWith(t => Log("DIVER READER DIED :((((((((((((((((((("));
            diverWriterTask.ContinueWith(t => Log("DIVER WRITER DIED :((((((((((((((((((("));

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
                var t1 = Task.Run(() => Reader(clientConnection, clientOut, src.Token)).ContinueWith(t => Log("CONSUMER READER DIED :((((((((((((((((((("));
                var t2 = Task.Run(() => Writer(clientConnection, clientIn, src.Token)).ContinueWith(t => Log("CONSUMER WRITER DIED :((((((((((((((((((("));

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
                Log("Client disconnected!!!");

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

    public static void Reader(TcpClient client, BlockingCollection<OverTheWireRequest> outQueue, CancellationToken token = default)
    {
        if (token == default)
            token = CancellationToken.None;

        Console.WriteLine($"[<->][{client?.Client?.RemoteEndPoint}] TransferData == IN ==");
        while (client.Connected && !token.IsCancellationRequested)
        {
            Console.WriteLine($"[<->][{client?.Client?.RemoteEndPoint}] waiting for more...");
            OverTheWireRequest recv = RnetProtocolParser.Parse(client, token);
            Console.WriteLine($"[<->][{client?.Client?.RemoteEndPoint}] New RECV message! ID: {recv?.RequestId}, PATH: {(recv.UrlAbsolutePath ?? "null")}");
            outQueue.Add(recv, token);
        }
        Console.WriteLine($"[<->][{client?.Client?.RemoteEndPoint}] TransferData == OUT ?!?!?!?!?! ==");
    }

    public static void Writer(TcpClient client, BlockingCollection<OverTheWireRequest> inQueue, CancellationToken token = default)
    {
        if (token == default)
            token = CancellationToken.None;

        Console.WriteLine($"[<->][{client?.Client?.RemoteEndPoint}] TransferData == IN ==");
        while (client.Connected && !token.IsCancellationRequested)
        {
            OverTheWireRequest toSend = inQueue.Take(token);
            Console.WriteLine($"[<->][{client?.Client?.RemoteEndPoint}] New TO_SEND message! ID: {toSend?.RequestId}, PATH: {(toSend.UrlAbsolutePath ?? "null")}");
            RnetProtocolParser.Write(client, toSend, token);
            Console.WriteLine($"[<->][{client?.Client?.RemoteEndPoint}] Written!");
        }
        Console.WriteLine($"[<->][{client?.Client?.RemoteEndPoint}] TransferData == OUT ?!?!?!?!?! ==");
    }

}
