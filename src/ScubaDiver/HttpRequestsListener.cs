using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using ScubaDiver.API.Protocol;
using ScubaDiver.Hooking;

namespace ScubaDiver;

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
        _task = Task.Run(Dispatcher);
    }

    public void Stop()
    {
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

        Logger.Debug("[DiverBase] HTTP Loop ended. Cleaning up");
    }

    private void HandleTcpClient(TcpClient client)
    {
        while (client.Connected)
        {
            var request = RnetProtocolParser.Parse(client);

            void RespondFunc(string body)
            {
                var resp = new OverTheWireRequest()
                {
                    RequestId = request.RequestId,
                    Body = body
                };
                RnetProtocolParser.Write(client, resp);
            }

            ScubaDiverMessage req =
                new ScubaDiverMessage(request.QueryString, request.UrlAbsolutePath, request.Body, RespondFunc);

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
        Logger.Debug($"[DotNetDiver] Listening on {listeningUrl}...");

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