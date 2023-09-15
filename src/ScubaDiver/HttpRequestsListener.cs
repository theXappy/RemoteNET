using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using ScubaDiver.API.Protocol;
using ScubaDiver.API.Protocol.SimpleHttp;
using ScubaDiver.Hooking;

namespace ScubaDiver;

public static class RnetRequestsListenerFactory
{
    public static IRequestsListener Create(ushort port, bool reverse)
    {
        Logger.Debug("[RnetRequestsListenerFactory] Create()");
        if (reverse)
        {
            Logger.Debug("[RnetRequestsListenerFactory] Create() - Reverse Listener");
            return new RnetReverseRequestsListener(port);
        }

        Logger.Debug("[RnetRequestsListenerFactory] Create() - Normal Listener");
        return new RnetRequestsListener(port);
    }
}

public class RnetReverseRequestsListener : IRequestsListener
{
    private readonly ManualResetEvent _bootstrapStayAlive = new(true);
    private int _port;
    private TcpClient _bootstrapClient;
    private Task _bootstrapTask = null;

    public event EventHandler<ScubaDiverMessage> RequestReceived;

    public RnetReverseRequestsListener(int reverseProxyPort)
    {
        _port = reverseProxyPort;
    }

    public void Start()
    {
        _bootstrapStayAlive.Set();
        _bootstrapClient = new TcpClient();
        var ipe = new IPEndPoint(IPAddress.Parse("127.0.0.1"), _port);
        _bootstrapClient.Connect(ipe);
        _bootstrapTask = Task.Run(BootstrapDispatcher);
    }

    public void Stop()
    {
        _bootstrapClient.Close();
        _bootstrapStayAlive.Reset();
    }

    private void BootstrapDispatcher()
    {
        var client = _bootstrapClient;

        // Introduce ourselves to the proxy
        HttpRequestSummary intro =
            HttpRequestSummary.FromJson("/proxy_intro", new NameValueCollection(), "{\"role\":\"diver_bootstrap\"}");
        SimpleHttpProtocolParser.WriteRequest(client, intro);

        var introResp = SimpleHttpProtocolParser.ReadResponse(client);
        if (introResp == null || !introResp.BodyString.Contains("\"status\":\"OK\""))
            throw new Exception("Diver couldn't register at Lifeboat");

        string listeningUrl = $"http://127.0.0.1:{_port}/";
        Logger.Debug($"[RnetReverseRequestsListener] Connected. Proxy should be available at: {listeningUrl}");

        List<TcpClient> aliveConsumers = new List<TcpClient>();

        while (_bootstrapStayAlive.WaitOne(TimeSpan.FromMilliseconds(100)) && client.Connected)
        {
            var request = SimpleHttpProtocolParser.ReadRequest(client);
            if (request == null)
                continue;

            if (request.Url != "/spawn_new_connection")
            {
                Console.WriteLine($"Forbidden URL at bootstrapper socket: {request.Url}");
                continue;
            }

            Console.WriteLine("[!] New spawn_new_connection request");

            // Local port decided for us by the proxy. This is its way to find us later.
            int port = int.Parse(request.QueryString["port"]);
            TcpClient consumerClient = new TcpClient(new IPEndPoint(IPAddress.Parse("127.0.0.1"), port));
            Console.WriteLine($"[+++] Adding {consumerClient.Client.RemoteEndPoint} to live list");
            aliveConsumers.Add(consumerClient);

            var ipe = new IPEndPoint(IPAddress.Parse("127.0.0.1"), _port);
            consumerClient.Connect(ipe);
            Task consumerTask = Task.Run(() => Dispatcher(consumerClient));
            consumerTask.ContinueWith(t =>
            {
                Console.WriteLine($"[XXX] Removing {consumerClient.Client.RemoteEndPoint} from live list");
                return aliveConsumers.Remove(consumerClient);
            });
        }
    }

    private void Dispatcher(TcpClient client)
    {
        // Introduce ourselves to the proxy
        HttpRequestSummary intro =
            HttpRequestSummary.FromJson("/proxy_intro", new NameValueCollection(), "{\"role\":\"diver\"}");
        SimpleHttpProtocolParser.WriteRequest(client, intro);
        client.GetStream().Flush();

        HttpResponseSummary introResp = null;
        try
        {
            introResp = SimpleHttpProtocolParser.ReadResponse(client);
        }
        catch(Exception ex)
        {
            Console.WriteLine("ERROR!!!!!!!!!");
            Console.WriteLine(ex);
            Console.WriteLine(ex.StackTrace);
            Debugger.Launch();
        }

        if (introResp == null || !introResp.BodyString.Contains("\"status\":\"OK\""))
            throw new Exception("Diver couldn't register at Lifeboat");

        while (_bootstrapStayAlive.WaitOne(TimeSpan.FromMilliseconds(100)) && client.Connected)
        {
            HttpRequestSummary request = SimpleHttpProtocolParser.ReadRequest(client);
            if (request == null)
                continue;

            void RespondFunc(string body)
            {
                Dictionary<string, string> headers = new Dictionary<string, string>();
                string requestId = request.QueryString.Get("requestId");
                if (!string.IsNullOrWhiteSpace(requestId))
                    headers["requestId"] = requestId;

                var resp = HttpResponseSummary.FromJson(HttpStatusCode.OK, body, headers);
                SimpleHttpProtocolParser.WriteResponse(client, resp);
            }

            ScubaDiverMessage req =
                new ScubaDiverMessage(request.QueryString, request.Url, request.BodyString, RespondFunc);

            RequestReceived?.Invoke(this, req);
        }
    }

    public void WaitForExit()
    {
        try
        {
            _bootstrapTask.Wait();
        }
        catch (Exception e)
        {
            Logger.Debug($"[RnetReverseRequestsListener] WaitForExit() -- Dispatcher task exited with exception. Ex: " + e);
            return;
        }
    }

    public void Dispose()
    {
        _bootstrapStayAlive?.Dispose();
        RequestReceived = null;
    }

}


public class RnetRequestsListener : IRequestsListener
{
    private readonly ManualResetEvent _stayAlive = new(true);
    private TcpListener _listener;
    private Task _task = null;

    public event EventHandler<ScubaDiverMessage> RequestReceived;

    public RnetRequestsListener(int listenPort)
    {
        _listener = new TcpListener(IPAddress.Any, listenPort);
    }


    public void Start()
    {
        _stayAlive.Set();
        _listener.Start();
        _task = Task.Run(Dispatcher);
    }

    public void Stop()
    {
        _listener.Stop();
        _stayAlive.Reset();
    }
    private void Dispatcher()
    {
        // Using a timeout we can make sure not to block if the
        // 'stayAlive' state changes to "reset" (which means we should die)
        while (_stayAlive.WaitOne(TimeSpan.FromMilliseconds(100)))
        {
            TcpClient client = _listener.AcceptTcpClient();
            Task.Run(() => HandleTcpClient(client));
        }
    }

    private void HandleTcpClient(TcpClient client)
    {
        while (_stayAlive.WaitOne(TimeSpan.FromMilliseconds(100)) && client.Connected)
        {
            var request = SimpleHttpProtocolParser.ReadRequest(client);
            if (request == null)
            {
                // Connection closed
                return;
            }

            void RespondFunc(string body)
            {
                Dictionary<string, string> headers = new Dictionary<string, string>();
                string requestId = request.QueryString.Get("requestId");
                if (!string.IsNullOrWhiteSpace(requestId))
                    headers["requestId"] = requestId;

                var resp = HttpResponseSummary.FromJson(HttpStatusCode.OK, body);
                SimpleHttpProtocolParser.WriteResponse(client, resp);
            }

            ScubaDiverMessage req =
                new ScubaDiverMessage(request.QueryString, request.Url, request.BodyString, RespondFunc);

            RequestReceived?.Invoke(this, req);
        }
    }

    public void WaitForExit()
    {
        try
        {
            _task.Wait();
        }
        catch
        {
        }
    }
    public void Dispose()
    {
        _stayAlive?.Dispose();
        RequestReceived = null;
    }
}

public class HttpRequestsListener : IRequestsListener
{
    private readonly ManualResetEvent _stayAlive = new(true);
    private HttpListener _listener;
    private Task _task = null;

    public event EventHandler<ScubaDiverMessage> RequestReceived;

    public HttpRequestsListener(int listenPort)
    {
        if (listenPort > ushort.MaxValue || listenPort < 0)
            throw new ArgumentException("Port out of range. Value: " + listenPort);

        HttpListener listener = new();
        string listeningUrl = $"http://127.0.0.1:{listenPort}/";
        listener.Prefixes.Add(listeningUrl);
        // Set timeout
        var manager = listener.TimeoutManager;
        manager.IdleConnection = TimeSpan.FromSeconds(5);
        listener.Start();
        Logger.Debug($"[HttpRequestsListener] Listening on {listeningUrl}...");

        _listener = listener;
    }

    public void Start()
    {
        _stayAlive.Set();
        _task = Task.Run(Dispatcher);
    }

    public void Stop()
    {
        _stayAlive.Reset();
    }

    public void WaitForExit()
    {
        try
        {
            _task?.Wait();
        }
        catch
        {
        }
    }

    private void Dispatcher()
    {
        // Using a timeout we can make sure not to block if the
        // 'stayAlive' state changes to "reset" (which means we should die)
        while (_stayAlive.WaitOne(TimeSpan.FromMilliseconds(100)))
        {
            IAsyncResult asyncOperation = _listener.BeginGetContext(ListenerCallback, _listener);

            while (true)
            {
                if (asyncOperation.AsyncWaitHandle.WaitOne(TimeSpan.FromMilliseconds(100)))
                {
                    // Async operation started! We can mov on to next request
                    break;
                }

                // Async event still awaiting new HTTP requests... It's a good time to check
                // if we were signaled to die
                if (!_stayAlive.WaitOne(TimeSpan.FromMilliseconds(100)))
                {
                    // Time to die.
                    // Leaving the inner loop will get us to the outter loop where _stayAlive is checked (again)
                    // and then it that loop will stop as well.
                    break;
                }
                // No singal of die command. We can continue waiting
            }
        }

        Logger.Debug("[DiverBase] HTTP Loop ended. Cleaning up");
    }

    private void ListenerCallback(IAsyncResult result)
    {
        try
        {
            HarmonyWrapper.Instance.RegisterFrameworkThread(Thread.CurrentThread.ManagedThreadId);

            HttpListener listener = (HttpListener)result.AsyncState;
            HttpListenerContext context;
            try
            {
                context = listener.EndGetContext(result);
            }
            catch (ObjectDisposedException)
            {
                Logger.Debug("[DiverBase][ListenerCallback] Listener was disposed. Exiting.");
                return;
            }
            catch (HttpListenerException e)
            {
                if (e.Message.StartsWith("The I/O operation has been aborted"))
                {
                    Logger.Debug($"[DiverBase][ListenerCallback] Listener was aborted. Exiting.");
                    return;
                }
                throw;
            }

            try
            {
                ScubaDiverMessage req = ParseRequest(context);
                RequestReceived(this, req);
            }
            catch (Exception e)
            {
                Console.WriteLine("[DiverBase] Task faulted! Exception:");
                Console.WriteLine(e);
            }
        }
        finally
        {
            HarmonyWrapper.Instance.UnregisterFrameworkThread(Thread.CurrentThread.ManagedThreadId);
        }
    }

    private static ScubaDiverMessage ParseRequest(HttpListenerContext requestContext)
    {
        var req = requestContext.Request;
        var response = requestContext.Response;
        string body;
        using (StreamReader sr = new(req.InputStream))
        {
            body = sr.ReadToEnd();
        }

        Action<string> responseSender = body =>
        {
            byte[] buffer = Encoding.UTF8.GetBytes(body);
            // Get a response stream and write the response to it.
            response.ContentLength64 = buffer.Length;
            response.ContentType = "application/json";
            Stream output = response.OutputStream;
            output.Write(buffer, 0, buffer.Length);
            // You must close the output stream.
            output.Close();
        };
        Dictionary<string, string> dict = req.QueryString.AllKeys.ToDictionary(key => key, key => req.QueryString.Get(key));

        return new ScubaDiverMessage(dict, req.Url.AbsolutePath, body, responseSender);
    }

    public void Dispose()
    {
        _stayAlive?.Dispose();
        RequestReceived = null;
    }
}