using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Reflection;
using System.Text;
using System.Threading;
using ScubaDiver.API.Interactions;
using ScubaDiver.API.Interactions.Client;
using ScubaDiver.API.Utils;
using ScubaDiver.Hooking;
using Exception = System.Exception;

namespace ScubaDiver
{
    public abstract class DiverBase : IDisposable
    {
        // Clients Tracking
        public object _registeredPidsLock = new();
        public List<int> _registeredPids = new();

        // HTTP Responses fields
        protected readonly Dictionary<string, Func<HttpListenerRequest, string>> _responseBodyCreators;

        private readonly ManualResetEvent _stayAlive = new(true);

        public DiverBase()
        {
            _responseBodyCreators = new Dictionary<string, Func<HttpListenerRequest, string>>()
            {
                // Divert maintenance
                {"/ping", MakePingResponse},
                {"/die", MakeDieResponse},
                {"/register_client", MakeRegisterClientResponse},
                {"/unregister_client", MakeUnregisterClientResponse},
                // DLL Injection
                {"/inject", MakeInjectResponse},
                // Dumping
                {"/domains", MakeDomainsResponse},
                {"/heap", MakeHeapResponse},
                {"/types", MakeTypesResponse},
                {"/type", MakeTypeResponse},
                // Remote Object API
                {"/object", MakeObjectResponse},
                {"/create_object", MakeCreateObjectResponse},
                {"/invoke", MakeInvokeResponse},
                {"/get_field", MakeGetFieldResponse},
                {"/set_field", MakeSetFieldResponse},
                {"/unpin", MakeUnpinResponse},
                {"/get_item", MakeArrayItemResponse},
            };
        }


        public abstract void Start(ushort listenPort);

        #region Helpers
        protected Assembly InitNewtonsoftJson()
        {
            // This will trigger our resolver to either get a pre-loaded Newtonsoft.Json version
            // (used by our target) or, if not found, load our own dll.
            Assembly ass = Assembly.Load(new AssemblyName("Newtonsoft.Json"));
            NewtonsoftProxy.Init(ass);
            return ass;
        }
        
        public string QuickError(string error, string stackTrace = null)
        {
            if (stackTrace == null)
            {
                stackTrace = (new StackTrace(true)).ToString();
            }
            DiverError errResults = new(error, stackTrace);
            return JsonConvert.SerializeObject(errResults);
        }

        #endregion

        #region HTTP Dispatching
        private void HandleDispatchedRequest(HttpListenerContext requestContext)
        {
            HttpListenerRequest request = requestContext.Request;

            var response = requestContext.Response;
            string body;
            if (_responseBodyCreators.TryGetValue(request.Url.AbsolutePath, out var respBodyGenerator))
            {
                try
                {
                    body = respBodyGenerator(request);
                }
                catch (Exception ex)
                {
                    body = QuickError(ex.Message, ex.StackTrace);
                }
            }
            else
            {
                body = QuickError("Unknown Command");
            }

            byte[] buffer = Encoding.UTF8.GetBytes(body);
            // Get a response stream and write the response to it.
            response.ContentLength64 = buffer.Length;
            response.ContentType = "application/json";
            Stream output = response.OutputStream;
                output.Write(buffer, 0, buffer.Length);
            // You must close the output stream.
            output.Close();
        }

        protected void Dispatcher(HttpListener listener)
        {
            // Using a timeout we can make sure not to block if the
            // 'stayAlive' state changes to "reset" (which means we should die)
            while (_stayAlive.WaitOne(TimeSpan.FromMilliseconds(100)))
            {
                void ListenerCallback(IAsyncResult result)
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
                            Logger.Debug("[DotNetDiver][ListenerCallback] Listener was disposed. Exiting.");
                            return;
                        }
                        catch (HttpListenerException e)
                        {
                            if (e.Message.StartsWith("The I/O operation has been aborted"))
                            {
                                Logger.Debug($"[DotNetDiver][ListenerCallback] Listener was aborted. Exiting.");
                                return;
                            }
                            throw;
                        }

                        try
                        {
                            HandleDispatchedRequest(context);
                        }
                        catch (Exception e)
                        {
                            Console.WriteLine("[DotNetDiver] Task faulted! Exception:");
                            Console.WriteLine(e);
                        }
                    }
                    finally
                    {
                        HarmonyWrapper.Instance.UnregisterFrameworkThread(Thread.CurrentThread.ManagedThreadId);
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
                        if (!_stayAlive.WaitOne(TimeSpan.FromMilliseconds(100)))
                        {
                            // Time to die.
                            // Leaving the inner loop will get us to the outter loop where _stayAlive is checked (again)
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

            Logger.Debug("[DotNetDiver] HTTP Loop ended. Cleaning up");
            this.DispatcherCleanUp();
        }

        protected abstract void DispatcherCleanUp();
        #endregion

        protected abstract string MakeInjectResponse(HttpListenerRequest req);

        #region Ping Handler

        private string MakePingResponse(HttpListenerRequest arg)
        {
            return "{\"status\":\"pong\"}";
        }

        #endregion

        #region Client Registration Handlers
        private string MakeRegisterClientResponse(HttpListenerRequest arg)
        {
            string pidString = arg.QueryString.Get("process_id");
            if (pidString == null || !int.TryParse(pidString, out int pid))
            {
                return QuickError("Missing parameter 'process_id'");
            }
            lock (_registeredPidsLock)
            {
                _registeredPids.Add(pid);
            }
            Logger.Debug("[DotNetDiver] New client registered. ID = " + pid);
            return "{\"status\":\"OK'\"}";
        }
        private string MakeUnregisterClientResponse(HttpListenerRequest arg)
        {
            string pidString = arg.QueryString.Get("process_id");
            if (pidString == null || !int.TryParse(pidString, out int pid))
            {
                return QuickError("Missing parameter 'process_id'");
            }
            bool removed;
            int remaining;
            lock (_registeredPidsLock)
            {
                removed = _registeredPids.Remove(pid);
                remaining = _registeredPids.Count;
            }
            Logger.Debug("[DotNetDiver] Client unregistered. ID = " + pid);

            UnregisterClientResponse ucResponse = new()
            {
                WasRemvoed = removed,
                OtherClientsAmount = remaining
            };

            return JsonConvert.SerializeObject(ucResponse);
        }

        #endregion

        protected abstract string MakeDomainsResponse(HttpListenerRequest req);
        protected abstract string MakeTypesResponse(HttpListenerRequest req);
        protected abstract string MakeTypeResponse(HttpListenerRequest req);
        protected abstract string MakeHeapResponse(HttpListenerRequest arg);
        protected abstract string MakeObjectResponse(HttpListenerRequest arg);
        protected abstract string MakeCreateObjectResponse(HttpListenerRequest arg);
        protected abstract string MakeInvokeResponse(HttpListenerRequest arg);
        protected abstract string MakeGetFieldResponse(HttpListenerRequest arg);
        protected abstract string MakeSetFieldResponse(HttpListenerRequest arg);
        protected abstract string MakeArrayItemResponse(HttpListenerRequest arg);
        protected abstract string MakeUnpinResponse(HttpListenerRequest arg);
        
        private string MakeDieResponse(HttpListenerRequest req)
        {
            Logger.Debug("[DotNetDiver] Die command received");
            bool forceKill = req.QueryString.Get("force")?.ToUpper() == "TRUE";
            lock (_registeredPidsLock)
            {
                if (_registeredPids.Count > 0 && !forceKill)
                {
                    Logger.Debug("[DotNetDiver] Die command failed - More clients exist.");
                    return "{\"status\":\"Error more clients remaining. You can use the force=true argument to ignore this check.\"}";
                }
            }

            Logger.Debug("[DotNetDiver] Die command accepted.");
            _stayAlive.Reset();
            return "{\"status\":\"Goodbye\"}";
        }
        public abstract void Dispose();
    }
}