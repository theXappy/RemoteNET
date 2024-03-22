using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using ScubaDiver.API.Hooking;
using ScubaDiver.API.Interactions;
using ScubaDiver.API.Interactions.Callbacks;
using ScubaDiver.API.Utils;
using static ScubaDiver.API.DiverCommunicator;

namespace ScubaDiver.API
{
    /// <summary>
    /// Listens for 'callbacks' invocations from the Diver - Callbacks for remote events and remote hooked functions
    /// </summary>
    public class CallbacksListener
    {
        private HttpListener _listener = null;
        Task _listenTask = null;
        CancellationTokenSource _src = null;

        public IPAddress IP { get; set; }
        public int Port { get; set; }
        readonly object _withErrors = NewtonsoftProxy.JsonSerializerSettingsWithErrors;
        private readonly Dictionary<int, LocalEventCallback> _tokensToEventHandlers = new();
        private readonly Dictionary<LocalEventCallback, int> _eventHandlersToToken = new();

        private readonly Dictionary<int, LocalHookCallback> _tokensToHookCallbacks = new();
        private readonly Dictionary<LocalHookCallback, int> _hookCallbacksToTokens = new();

        DiverCommunicator _communicator;

        public CallbacksListener(DiverCommunicator communicator)
        {
            _communicator = communicator;
            // Generate a random port with a temporary TcpListener
            int GetRandomUnusedPort()
            {
                var listener = new TcpListener(IPAddress.Any, 0);
                listener.Start();
                var port = ((IPEndPoint)listener.LocalEndpoint).Port;
                listener.Stop();
                return port;
            }

            Port = GetRandomUnusedPort();
            IP = IPAddress.Parse("127.0.0.1");
        }

        public bool IsOpen { get; private set; }
        public bool HasActiveHooks => _tokensToEventHandlers.Count > 0 || _tokensToHookCallbacks.Count > 0;

        public void Open()
        {
            if (!IsOpen)
            {
                // Need to create HTTP listener and send the Diver it's info
                _listener = new HttpListener();
                string listeningUrl = $"http://{IP}:{Port}/";
                _listener.Prefixes.Add(listeningUrl);
                _listener.Start();
                _src = new CancellationTokenSource();
                _listenTask = Task.Factory.StartNew(() => Dispatcher(_listener), _src.Token, TaskCreationOptions.AttachedToParent, TaskScheduler.Default);
                IsOpen = true;
            }
        }

        public void Close()
        {
            if (IsOpen)
            {
                _src.Cancel();
                try
                {
                    _listenTask.Wait();
                }
                catch { }
                _listener.Close();
                _src = null;
                _listener = null;
                _listenTask = null;

                IsOpen = false;
            }
        }

        private void Dispatcher(HttpListener listener)
        {
            while (_src != null && !_src.IsCancellationRequested)
            {
                void ListenerCallback(IAsyncResult result)
                {
                    HttpListener listener = (HttpListener)result.AsyncState;
                    HttpListenerContext context;
                    try
                    {
                        context = listener.EndGetContext(result);
                    }
                    catch (ObjectDisposedException)
                    {
                        return;
                    }
                    catch (System.Net.HttpListenerException)
                    {
                        // Sometimes happen at teardown. Maybe there's a race condition here and waiting on something
                        // can prevent this but I don't really care
                        return;
                    }

                    try
                    {
                        HandleDispatchedRequest(context);
                    }
                    catch (Exception e)
                    {
                        Console.WriteLine("[CallbacksListener] Error when process outoing request! Exception:");
                        Console.WriteLine(e);
                    }
                }
                IAsyncResult asyncOperation = listener.BeginGetContext(ListenerCallback, listener);

                while (true)
                {
                    if (asyncOperation.AsyncWaitHandle.WaitOne(TimeSpan.FromMilliseconds(100)))
                    {
                        // Async operation started! We can mov on to next request
                        break;
                    }
                    else
                    {
                        // Async event still awaiting new HTTP requests... It's a good time to check
                        // if we were signaled to die
                        if (_src.Token.WaitHandle.WaitOne(TimeSpan.FromMilliseconds(100)))
                        {
                            // Time to die.
                            // Leaving the inner loop will get us to the outter loop where _src is checked (again)
                            // and then it that loop will stop as well.
                            break;
                        }
                        else
                        {
                            // No singal of die command. We can continue waiting
                            continue;
                        }
                    }
                }
            }
        }

        private void HandleDispatchedRequest(HttpListenerContext context)
        {
            HttpListenerRequest request = context.Request;

            var response = context.Response;
            string body = null;
            if (request.Url.AbsolutePath == "/ping")
            {
                string pongRes = "{\"status\":\"pong\"}";
                byte[] pongResBytes = System.Text.Encoding.UTF8.GetBytes(pongRes);
                // Get a response stream and write the response to it.
                response.ContentLength64 = pongResBytes.Length;
                response.ContentType = "application/json";
                Stream outputStream = response.OutputStream;
                outputStream.Write(pongResBytes, 0, pongResBytes.Length);
                // You must close the output stream.
                outputStream.Close();
                return;
            }
            if (request.Url.AbsolutePath == "/invoke_callback")
            {
                using (StreamReader sr = new(request.InputStream))
                {
                    body = sr.ReadToEnd();
                }
                CallbackInvocationRequest res = JsonConvert.DeserializeObject<CallbackInvocationRequest>(body, _withErrors);
                if (_tokensToEventHandlers.TryGetValue(res.Token, out LocalEventCallback callbackFunction))
                {
                    (bool voidReturnType, ObjectOrRemoteAddress callbackRes) = callbackFunction(res.Parameters.ToArray());

                    InvocationResults ir = new()
                    {
                        VoidReturnType = voidReturnType,
                        ReturnedObjectOrAddress = voidReturnType ? null : callbackRes
                    };

                    body = JsonConvert.SerializeObject(ir);
                }
                else if (_tokensToHookCallbacks.TryGetValue(res.Token, out LocalHookCallback hook))
                {
                    HookContext hookContext = new(res.StackTrace, res.ThreadID);

                    // Run hook. No results expected directly (it might alter variabels inside the hook)
                    hook(hookContext, res.Parameters.FirstOrDefault(), res.Parameters.Skip(1).ToArray());

                    // Report back whether to call the original function or no (Harmony wants this as the return value)
                    InvocationResults ir = new()
                    {
                        VoidReturnType = false,
                        ReturnedObjectOrAddress = ObjectOrRemoteAddress.FromObj(hookContext.skipOriginal)
                    };

                    body = JsonConvert.SerializeObject(ir);
                }
                else
                {
                    Console.WriteLine($"[WARN] Diver tried to trigger a callback with unknown token value: {res.Token}");

                    // TODO: I'm not sure the usage of 'DiverError' here is good. It's sent from the Communicator's side
                    // to the Diver's side...
                    DiverError errResults = new("Unknown Token", String.Empty);
                    body = JsonConvert.SerializeObject(errResults);
                }
            }
            else
            {
                Console.WriteLine($"[WARN] Diver tried to trigger an unexpected path: {request.Url.AbsolutePath}");
                // TODO: I'm not sure the usage of 'DiverError' here is good. It's sent from the Communicator's side
                // to the Diver's side...
                DiverError errResults = new("Unknown path in URL", String.Empty);
                body = JsonConvert.SerializeObject(errResults);
            }

            byte[] buffer = System.Text.Encoding.UTF8.GetBytes(body);
            // Get a response stream and write the response to it.
            response.ContentLength64 = buffer.Length;
            response.ContentType = "application/json";
            Stream output = response.OutputStream;
            output.Write(buffer, 0, buffer.Length);
            // You must close the output stream.
            output.Close();
        }

        public void EventSubscribe(LocalEventCallback callback, int token)
        {
            _tokensToEventHandlers[token] = callback;
            _eventHandlersToToken[callback] = token;
        }

        public int EventUnsubscribe(LocalEventCallback callback)
        {
            if (_eventHandlersToToken.TryGetValue(callback, out int token))
            {
                _tokensToEventHandlers.Remove(token);
                _eventHandlersToToken.Remove(callback);
                return token;
            }
            else
            {
                throw new Exception($"[CallbackListener] EventUnsubscribe TryGetValue failed :(((((((((((((((");
            }
        }

        public void HookSubscribe(LocalHookCallback callback, int token)
        {
            _tokensToHookCallbacks[token] = callback;
            _hookCallbacksToTokens[callback] = token;
        }

        public int HookUnsubscribe(LocalHookCallback callback)
        {
            if (_hookCallbacksToTokens.TryGetValue(callback, out int token))
            {
                _tokensToHookCallbacks.Remove(token);
                _hookCallbacksToTokens.Remove(callback);
                return token;
            }
            else
            {
                throw new Exception($"[CallbackListener] HookUnsubscribe TryGetValue failed :(((((((((((((((");
            }
        }
    }
}
