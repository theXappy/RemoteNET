using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Diagnostics;
using System.Net;
using System.Reflection;
using ScubaDiver.API.Interactions;
using ScubaDiver.API.Interactions.Client;
using ScubaDiver.API.Utils;
using Exception = System.Exception;

namespace ScubaDiver
{
    public abstract class DiverBase : IDisposable
    {
        // Clients Tracking
        public object _registeredPidsLock = new();
        public List<int> _registeredPids = new();

        // HTTP Responses fields
        protected readonly Dictionary<string, Func<ScubaDiverMessage, string>> _responseBodyCreators;
        private IRequestsListener _listener;

        public DiverBase(IRequestsListener listener)
        {
            _listener = listener;
            _responseBodyCreators = new Dictionary<string, Func<ScubaDiverMessage, string>>()
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

        public virtual void Start()
        {
            _listener.RequestReceived += HandleDispatchedRequest;
            _listener.Start();
        }

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

        private void HandleDispatchedRequest(object obj, ScubaDiverMessage request)
        {
            string body;
            if (_responseBodyCreators.TryGetValue(request.UrlAbsolutePath, out var respBodyGenerator))
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

            request.ResponseSender(body);
        }
        #endregion

        protected abstract string MakeInjectResponse(ScubaDiverMessage req);

        #region Ping Handler

        private string MakePingResponse(ScubaDiverMessage arg)
        {
            return "{\"status\":\"pong\"}";
        }

        #endregion

        #region Client Registration Handlers
        private string MakeRegisterClientResponse(ScubaDiverMessage arg)
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
            Logger.Debug("[DiverBase] New client registered. ID = " + pid);
            return "{\"status\":\"OK\"}";
        }
        private string MakeUnregisterClientResponse(ScubaDiverMessage arg)
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
            Logger.Debug("[DiverBase] Client unregistered. ID = " + pid);

            UnregisterClientResponse ucResponse = new()
            {
                WasRemvoed = removed,
                OtherClientsAmount = remaining
            };

            return JsonConvert.SerializeObject(ucResponse);
        }

        #endregion

        protected abstract string MakeDomainsResponse(ScubaDiverMessage req);
        protected abstract string MakeTypesResponse(ScubaDiverMessage req);
        protected abstract string MakeTypeResponse(ScubaDiverMessage req);
        protected abstract string MakeHeapResponse(ScubaDiverMessage arg);
        protected abstract string MakeObjectResponse(ScubaDiverMessage arg);
        protected abstract string MakeCreateObjectResponse(ScubaDiverMessage arg);
        protected abstract string MakeInvokeResponse(ScubaDiverMessage arg);
        protected abstract string MakeGetFieldResponse(ScubaDiverMessage arg);
        protected abstract string MakeSetFieldResponse(ScubaDiverMessage arg);
        protected abstract string MakeArrayItemResponse(ScubaDiverMessage arg);
        protected abstract string MakeUnpinResponse(ScubaDiverMessage arg);

        private string MakeDieResponse(ScubaDiverMessage req)
        {
            Logger.Debug("[DiverBase] Die command received");
            bool forceKill = req.QueryString.Get("force")?.ToUpper() == "TRUE";
            lock (_registeredPidsLock)
            {
                if (_registeredPids.Count > 0 && !forceKill)
                {
                    Logger.Debug("[DiverBase] Die command failed - More clients exist.");
                    return "{\"status\":\"Error more clients remaining. You can use the force=true argument to ignore this check.\"}";
                }
            }

            Logger.Debug("[DiverBase] Die command accepted.");
            _listener.Stop();
            return "{\"status\":\"Goodbye\"}";
        }


        public virtual void Dispose()
        {
            _listener.Stop();
            _listener.RequestReceived -= HandleDispatchedRequest;
            _listener.Dispose();
        }

        public void WaitForExit() => _listener.WaitForExit();
    }


}