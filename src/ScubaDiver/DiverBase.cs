using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Reflection;
using ScubaDiver.API;
using System.Threading;
using ScubaDiver.API.Interactions;
using ScubaDiver.API.Interactions.Callbacks;
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
        protected readonly Dictionary<string, Func<ScubaDiverMessage, string>> _responseBodyCreators;
        private IRequestsListener _listener;

        // Hooks Tracking
        protected bool _monitorEndpoints = true;
        private int _nextAvailableCallbackToken;
        protected readonly ConcurrentDictionary<int, RegisteredManagedMethodHookInfo> _remoteHooks;

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
                {"/help", MakeHelpResponse},
                {"/launch_debugger", MakeLaunchDebuggerResponse},
                // DLL Injection
                {"/inject_assembly", MakeInjectAssemblyResponse},
                {"/inject_dll", MakeInjectDllResponse},
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
                // Hooking
                {"/hook_method", MakeHookMethodResponse},
                {"/unhook_method", MakeUnhookMethodResponse},
                // Custom Functions
                {"/register_custom_function", MakeRegisterCustomFunctionResponse},
            };
            _remoteHooks = new ConcurrentDictionary<int, RegisteredManagedMethodHookInfo>();
        }

        private string MakeHelpResponse(ScubaDiverMessage arg)
        {
            var possibleCommands = _responseBodyCreators.Keys.ToList();
            possibleCommands.Sort();
            return JsonConvert.SerializeObject(possibleCommands);
        }

        public virtual void Start()
        {
            Logger.Debug("[DiverBase] Start() -- entering");
            _listener.RequestReceived += HandleDispatchedRequest;
            _listener.Start();
            Logger.Debug("[DiverBase] Start() -- returning");
        }
        protected virtual void CallbacksEndpointsMonitor()
        {
            while (_monitorEndpoints)
            {
                Thread.Sleep(TimeSpan.FromSeconds(1));
                IPEndPoint endpoint;
                foreach (var registeredMethodHookInfo in _remoteHooks)
                {
                    endpoint = registeredMethodHookInfo.Value.Endpoint;
                    ReverseCommunicator reverseCommunicator = new(endpoint);
                    //Logger.Debug($"[DiverBase] Checking if callback client at {endpoint} is alive. Token = {registeredMethodHookInfo.Key}. Type = Method Hook");
                    bool alive = reverseCommunicator.CheckIfAlive();
                    //Logger.Debug($"[DiverBase] Callback client at {endpoint} (Token = {registeredMethodHookInfo.Key}) is alive = {alive}");
                    if (!alive)
                    {
                        Logger.Debug($"[DiverBase] Dead Callback client at {endpoint} (Token = {registeredMethodHookInfo.Key}) DROPPED!");
                        _remoteHooks.TryRemove(registeredMethodHookInfo.Key, out _);
                    }
                }
            }
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
            Stopwatch sw = Stopwatch.StartNew();
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
            sw.Stop();

            request.ResponseSender(body);
        }
        #endregion

        #region DLL Injecion Handler

        protected abstract string MakeInjectDllResponse(ScubaDiverMessage req);
        protected virtual string MakeInjectAssemblyResponse(ScubaDiverMessage req)
        {
            string dllPath = req.QueryString.Get("dll_path");
            try
            {
                var asm = Assembly.LoadFile(dllPath);
                // We must request all Types or otherwise the Type object won't be created
                // (I think there's some lazy initialization behind the scenes)
                var allTypes = asm.GetTypes();
                // This will prevent the compiler from removing the above lines because of "unused code"
                GC.KeepAlive(allTypes);

                // ClrMD must take a new snapshot to see our new assembly
                RefreshRuntime();

                return "{\"status\":\"dll loaded\"}";
            }
            catch (Exception ex)
            {
                return QuickError(ex.Message, ex.StackTrace);
            }
        }

        protected abstract void RefreshRuntime();

        #endregion


        protected string MakeUnhookMethodResponse(ScubaDiverMessage arg)
        {
            string tokenStr = arg.QueryString.Get("token");
            if (tokenStr == null || !int.TryParse(tokenStr, out int token))
            {
                return QuickError("Missing parameter 'address'");
            }
            Logger.Debug($"[DiverBase][MakeUnhookMethodResponse] Called! Token: {token}");

            if (_remoteHooks.TryRemove(token, out RegisteredManagedMethodHookInfo rmhi))
            {
                rmhi.UnhookAction();
                return "{\"status\":\"OK\"}";
            }

            Logger.Debug($"[DiverBase][MakeUnhookMethodResponse] Unknown token for event callback subscription. Token: {token}");
            return QuickError("Unknown token for event callback subscription");
        }
        protected string MakeHookMethodResponse(ScubaDiverMessage arg)
        {
            Logger.Debug("[DiverBase] Got Hook Method request!");
            if (string.IsNullOrEmpty(arg.Body))
                return QuickError("Missing body");

            var request = JsonConvert.DeserializeObject<FunctionHookRequest>(arg.Body);
            if (request == null)
                return QuickError("Failed to deserialize body");

            if (!IPAddress.TryParse(request.IP, out IPAddress ipAddress))
                return QuickError("Failed to parse IP address. Input: " + request.IP);

            int port = request.Port;
            IPEndPoint endpoint = new IPEndPoint(ipAddress, port);
            Logger.Debug($"[DiverBase][MakeHookMethodResponse] Hook Method request - endpoint: {endpoint}");

            return HookFunctionWrapper(request, endpoint);
        }

        private string HookFunctionWrapper(FunctionHookRequest req, IPEndPoint endpoint)
        {
            // We're all good regarding the signature!
            // assign subscriber unique id
            int token = AssignCallbackToken();
            Logger.Debug($"[DiverBase] Hook Method - Assigned Token: {token}");
            Logger.Debug($"[DiverBase] Hook Method - endpoint: {endpoint}");


            // Preparing a proxy method that Harmony will invoke
            HarmonyWrapper.HookCallback patchCallback = (object obj, object[] args, ref object retValue) =>
            {
                object[] parameters = new object[args.Length + 1];
                parameters[0] = obj;
                Array.Copy(args, 0, parameters, 1, args.Length);

                // Shift control to remote hook (Other process)
                HookResponse res = InvokeHookCallback(endpoint, token, new StackTrace().ToString(), retValue, parameters: parameters);

                // Remote hook returned, examine it's return value.
                bool skipOriginal = res.SkipOriginal;
                if (res.ReturnValue != null)
                {
                    retValue = ResolveHookReturnValue(res.ReturnValue);
                }

                // Silly mix up...
                bool callOriginal = !skipOriginal;
                return callOriginal;
            };

            Logger.Debug($"[DiverBase] Hooking function {req.MethodName}...");
            Action unhookAction;
            try
            {
                unhookAction = HookFunction(req, patchCallback);
            }
            catch (Exception ex)
            {
                // Hooking filed so we cleanup the Hook Info we inserted beforehand 
                _remoteHooks.TryRemove(token, out _);

                Logger.Debug($"[DiverBase] Failed to hook func {req.MethodName}. Exception: {ex}");
                return QuickError($"Failed insert the hook for the function. HarmonyWrapper.AddHook failed. Exception: {ex}", ex.StackTrace);
            }

            Logger.Debug($"[DiverBase] Hooked func {req.MethodName}!");

            // Keeping all hooking information aside so we can unhook later.
            _remoteHooks[token] = new RegisteredManagedMethodHookInfo()
            {
                Endpoint = endpoint,
                RegisteredProxy = patchCallback,
                UnhookAction = unhookAction
            };

            EventRegistrationResults erResults = new() { Token = token };
            return JsonConvert.SerializeObject(erResults);
        }

        public abstract object ResolveHookReturnValue(ObjectOrRemoteAddress oora);

        public int AssignCallbackToken() => Interlocked.Increment(ref _nextAvailableCallbackToken);

        protected abstract Action HookFunction(FunctionHookRequest req, HarmonyWrapper.HookCallback patchCallback);

        protected abstract ObjectOrRemoteAddress InvokeEventCallback(IPEndPoint callbacksEndpoint, int token, string stackTrace, object retValue, params object[] parameters);
        protected abstract HookResponse InvokeHookCallback(IPEndPoint callbacksEndpoint, int token, string stackTrace, object retValue, params object[] parameters);



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

        #region Debugger Handler

        private string MakeLaunchDebuggerResponse(ScubaDiverMessage arg)
        {
            try
            {
                Debugger.Launch();
                return "{\"status\":\"debugger launched\"}";
            }
            catch (Exception ex)
            {
                return QuickError(ex.Message, ex.StackTrace);
            }
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
        protected abstract string MakeRegisterCustomFunctionResponse(ScubaDiverMessage arg);

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