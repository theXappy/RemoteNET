using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Web;
using ScubaDiver.API.Exceptions;
using ScubaDiver.API.Hooking;
using ScubaDiver.API.Interactions;
using ScubaDiver.API.Interactions.Callbacks;
using ScubaDiver.API.Interactions.Dumps;
using ScubaDiver.API.Interactions.Object;
using ScubaDiver.API.Protocol;
using ScubaDiver.API.Protocol.SimpleHttp;
using ScubaDiver.API.Utils;

namespace ScubaDiver.API
{
    /// <summary>
    /// Communicates with a diver in a remote process
    /// </summary>
    public class DiverCommunicator : IDisposable
    {
        readonly object _withErrors = NewtonsoftProxy.JsonSerializerSettingsWithErrors;

        private readonly string _hostname;
        public int DiverPort { get; private set; }

        private int? _process_id = null;
        private CallbacksListener _listener;
        private object _httpClientLock = new object();
        private ConcurrentHttpClient? _httpClient;
        private int _timeout;

        public DiverCommunicator(string hostname, int diverPort, int timeout = -1)
        {
            _hostname = hostname;
            DiverPort = diverPort;
            _listener = new CallbacksListener(this);
            _timeout = timeout;
        }

        public DiverCommunicator(IPAddress ipa, int diverPort) : this(ipa.ToString(), diverPort) { }
        public DiverCommunicator(IPEndPoint ipe) : this(ipe.Address, ipe.Port) { }

        private void Init()
        {
            if (_httpClient != null)
                return;
            lock (_httpClientLock)
            {
                if (_httpClient == null)
                {
                    TcpClient tcpClient = new TcpClient();
                    if (IPAddress.TryParse(_hostname, out IPAddress? ip))
                    {
                        tcpClient.Connect(new IPEndPoint(ip, DiverPort));
                    }
                    else
                    {
                        tcpClient.Connect(_hostname, DiverPort);
                    }

                    _httpClient = new ConcurrentHttpClient(tcpClient, _timeout);
                }
            }
        }

        private string SendRequest(string path, Dictionary<string, string> queryParams = null, string jsonBody = null)
        {
            Init();

            queryParams ??= new();

            HttpRequestSummary reqSummary = HttpRequestSummary.FromJson(path, queryParams, jsonBody);
            HttpResponseSummary response = _httpClient.Send(reqSummary);

            if (response == null)
            {
                throw new Exception(
                    $"Failed to read response, connection closed prematurely");
            }

            if (response.StatusCode != HttpStatusCode.OK)
            {
                lock (_httpClientLock)
                {
                    _httpClient.Dispose();
                    _httpClient = null;
                }
            }

            string body = Encoding.UTF8.GetString(response.Body);
            if (body.StartsWith("{\"error\":", StringComparison.InvariantCultureIgnoreCase))
            {
                // Diver sent back an error. We parse it here and throwing a 'proxied' exception
                var errMessage = JsonConvert.DeserializeObject<DiverError>(body, _withErrors);
                if (errMessage != null)
                    throw new RemoteException(errMessage.Error, errMessage.StackTrace);
            }


            return body;
        }

        public bool KillDiver()
        {
            if (_process_id.HasValue)
            {
                UnregisterClient(_process_id.Value);
            }

            string body = SendRequest("die");
            return body?.Contains("Goodbye") ?? false;
        }

        public bool InjectAssembly(string path)
        {
            var res = SendRequest("inject_assembly", new Dictionary<string, string>()
            {
                { "dll_path", path }
            });

            return res.Contains("dll loaded");
        }
        public bool InjectDll(string path)
        {
            var res = SendRequest("inject_dll", new Dictionary<string, string>()
            {
                { "dll_path", path }
            });

            return res.Contains("dll loaded");
        }

        /// <summary>
        /// Dumps the heap of the remote process
        /// </summary>
        /// <param name="typeFilter">TypeFullName filter of objects to get from the heap. Support leading/trailing wildcard (*). NULL returns all objects</param>
        /// <returns></returns>
        public HeapDump DumpHeap(string typeFilter = null, bool dumpHashcodes = true)
        {
            Dictionary<string, string> queryParams = new();
            if (typeFilter != null)
            {
                queryParams["type_filter"] = typeFilter;
            }
            queryParams["dump_hashcodes"] = dumpHashcodes.ToString();
            string body = SendRequest("heap", queryParams);
            HeapDump heapDump = JsonConvert.DeserializeObject<HeapDump>(body);
            return heapDump;
        }
        public DomainsDump DumpDomains()
        {
            string body = SendRequest("domains", null);
            DomainsDump? results = JsonConvert.DeserializeObject<DomainsDump>(body, _withErrors);

            return results;
        }

        public TypesDump DumpTypes(string typeFullNameFilter, string importerModule = null)
        {
            Dictionary<string, string> queryParams = new() { };
            queryParams["type_filter"] = typeFullNameFilter;
            if (!string.IsNullOrEmpty(importerModule))
            {
                queryParams["importer_module"] = importerModule;
            }

            string body = SendRequest("types", queryParams);
            TypesDump? results = JsonConvert.DeserializeObject<TypesDump>(body, _withErrors);

            return results;
        }

        public TypeDump DumpType(string type, string assembly = null)
        {
            TypeDumpRequest dumpRequest = new()
            {
                TypeFullName = type
            };
            if (assembly != null)
            {
                dumpRequest.Assembly = assembly;
            }
            var requestJsonBody = JsonConvert.SerializeObject(dumpRequest);

            string body = SendRequest("type", null, requestJsonBody);
            TypeDump? results = JsonConvert.DeserializeObject<TypeDump>(body, _withErrors);

            return results;
        }
        public TypeDump DumpType(long methodTableAddress)
        {
            TypeDumpRequest dumpRequest = new()
            {
                MethodTableAddress = methodTableAddress
            };
            var requestJsonBody = JsonConvert.SerializeObject(dumpRequest);

            string body = SendRequest("type", null, requestJsonBody);
            TypeDump? results = JsonConvert.DeserializeObject<TypeDump>(body, _withErrors);

            return results;
        }


        public ObjectDump DumpObject(ulong address, string typeName, bool pinObject = false, int? hashcode = null)
        {
            Dictionary<string, string> queryParams = new()
            {
                { "address", address.ToString() },
                { "type_name", typeName },
                { "pinRequest", pinObject.ToString() },
                { "hashcode_fallback", "false" }
            };
            if (hashcode.HasValue)
            {
                queryParams["hashcode"] = hashcode.Value.ToString();
                queryParams["hashcode_fallback"] = "true";
            }
            string body = SendRequest("object", queryParams);
            if (body.Contains("\"error\":"))
            {
                if (body.Contains("'address' points at an invalid address") ||
                    body.Contains("Method Table value mismatched"))
                {
                    throw new RemoteObjectMovedException(address, body);
                }
                throw new Exception("Diver failed to dump objet. Error: " + body);
            }
            ObjectDump objectDump = JsonConvert.DeserializeObject<ObjectDump>(body);
            return objectDump;
        }

        public bool UnpinObject(ulong address)
        {
            Dictionary<string, string> queryParams = new()
            {
                { "address", address.ToString() },
            };
            string body = SendRequest("unpin", queryParams);
            return body.Contains("OK");
        }

        public InvocationResults InvokeMethod(ulong targetAddr, string targetTypeFullName, string methodName,
            string[] genericArgsFullTypeNames,
            params ObjectOrRemoteAddress[] args)
        {
            InvocationRequest invocReq = new()
            {
                ObjAddress = targetAddr,
                TypeFullName = targetTypeFullName,
                MethodName = methodName,
                GenericArgsTypeFullNames = genericArgsFullTypeNames,
                Parameters = args.ToList()
            };
            var requestJsonBody = JsonConvert.SerializeObject(invocReq);

            var resJson = SendRequest("invoke", null, requestJsonBody);

            InvocationResults res;
            try
            {
                res = JsonConvert.DeserializeObject<InvocationResults>(resJson, _withErrors);
            }
            catch
            {
                Console.WriteLine($"[Communicator] Error when deserializing object! res: {resJson}");
                return null;
            }
            return res;
        }


        public bool RegisterClient(int? process_id = null)
        {
            _process_id = process_id ?? Process.GetCurrentProcess().Id;

            try
            {
                for (int i = 0; i < 10; i++)
                {
                    Debug.WriteLine($"[@@@][RegisterClient] Trying to register try #{i + 1}");
                    string body = SendRequest("register_client",
                        new Dictionary<string, string> { { "process_id", _process_id.Value.ToString() } });
                    if (body.Contains("{\"status\":\"OK\"}"))
                    {
                        // Success
                        return true;
                    }
                    else if (body.Contains("{\"status\":\"reject"))
                    {
                        Debug.WriteLine($"[@@@][RegisterClient] Trying to register try #{i + 1} -- rejected (too early)");
                        // We're probably too early and the Diver didn't communicate with Lifeboat yet.
                        // sleep and re-try
                        Thread.Sleep(TimeSpan.FromSeconds(1));
                        continue;
                    }
                    else
                    {
                        // Something weird with the respond
                        Debug.WriteLine("[@@@][RegisterClient] Unexpected response from Diver/Lifeboat when registering: " + body);
                        return false;
                    }
                }
                return false;
            }
            catch
            {
                return false;
            }
        }
        public bool UnregisterClient(int? process_id = null)
        {
            _process_id = process_id ?? Process.GetCurrentProcess().Id;

            try
            {
                string body = SendRequest("unregister_client", new Dictionary<string, string> { { "process_id", _process_id.Value.ToString() } });
                return body.Contains("{\"status\":\"OK'\"}");
            }
            catch
            {
                return false;
            }
            finally
            {
                _process_id = null;
            }
        }

        public bool CheckAliveness()
        {
            try
            {
                string body = SendRequest("ping");
                return body.Contains("pong");
            }
            catch
            {
                return false;
            }
        }

        public bool LaunchDebugger()
        {
            try
            {
                string body = SendRequest("launch_debugger");
                return body.Contains("debugger launched");
            }
            catch
            {
                return false;
            }
        }

        public ObjectOrRemoteAddress GetItem(ulong token, ObjectOrRemoteAddress key)
        {
            IndexedItemAccessRequest indexedItemAccess = new()
            {
                CollectionAddress = token,
                PinRequest = true,
                Index = key
            };
            var requestJsonBody = JsonConvert.SerializeObject(indexedItemAccess);

            var body = SendRequest("get_item", null, requestJsonBody);

            if (body.Contains("\"error\":"))
            {
                throw new Exception("Diver failed to dump item of remote collection object. Error: " + body);
            }
            InvocationResults invokeRes = JsonConvert.DeserializeObject<InvocationResults>(body);
            return invokeRes.ReturnedObjectOrAddress;

        }

        public InvocationResults InvokeStaticMethod(string targetTypeFullName, string methodName,
            params ObjectOrRemoteAddress[] args) =>
            InvokeStaticMethod(targetTypeFullName, methodName, null, args);

        public InvocationResults InvokeStaticMethod(string targetTypeFullName, string methodName,
            string[] genericArgsFullTypeNames,
            params ObjectOrRemoteAddress[] args) => InvokeMethod(0, targetTypeFullName, methodName, genericArgsFullTypeNames, args);

        public InvocationResults CreateObject(string typeFullName, ObjectOrRemoteAddress[] args)
        {
            var ctorInvocReq = new CtorInvocationRequest()
            {
                TypeFullName = typeFullName,
                Parameters = args.ToList()
            };
            var requestJsonBody = JsonConvert.SerializeObject(ctorInvocReq);

            var resJson = SendRequest("create_object", null, requestJsonBody);
            InvocationResults res = JsonConvert.DeserializeObject<InvocationResults>(resJson, _withErrors);
            return res;
        }

        public bool StartOffensiveGC(string assembly)
        {
            var resJson = SendRequest("gc", new Dictionary<string, string>() { ["assembly"] = assembly });
            return resJson.Contains("\"ok\"");
        }

        public InvocationResults SetField(ulong targetAddr, string targetTypeFullName, string fieldName, ObjectOrRemoteAddress newValue)
        {
            FieldSetRequest invocReq = new()
            {
                ObjAddress = targetAddr,
                TypeFullName = targetTypeFullName,
                FieldName = fieldName,
                Value = newValue
            };
            var requestJsonBody = JsonConvert.SerializeObject(invocReq);

            var resJson = SendRequest("set_field", null, requestJsonBody);

            InvocationResults res = JsonConvert.DeserializeObject<InvocationResults>(resJson, _withErrors);
            return res;
        }

        public InvocationResults GetField(ulong targetAddr, string targetTypeFullName, string fieldName)
        {
            FieldGetRequest invocReq = new()
            {
                ObjAddress = targetAddr,
                TypeFullName = targetTypeFullName,
                FieldName = fieldName,
            };
            var requestJsonBody = JsonConvert.SerializeObject(invocReq);

            var resJson = SendRequest("get_field", null, requestJsonBody);

            InvocationResults res = JsonConvert.DeserializeObject<InvocationResults>(resJson, _withErrors);
            return res;
        }

        public void EventSubscribe(ulong targetAddr, string eventName, LocalEventCallback callback)
        {
            Dictionary<string, string> queryParams;
            string body;
            if (!_listener.IsOpen)
            {
                _listener.Open();

            }

            queryParams = new()
            {
                ["address"] = targetAddr.ToString(),
                ["event"] = eventName,
                ["ip"] = _listener.IP.ToString(),
                ["port"] = _listener.Port.ToString()
            };
            body = SendRequest("event_subscribe", queryParams);
            EventRegistrationResults regRes = JsonConvert.DeserializeObject<EventRegistrationResults>(body);
            _listener.EventSubscribe(callback, regRes.Token);
        }

        public void EventUnsubscribe(LocalEventCallback callback)
        {
            int token = _listener.EventUnsubscribe(callback);

            Dictionary<string, string> queryParams = new();
            queryParams["token"] = token.ToString();
            string body = SendRequest("event_unsubscribe", queryParams);
            if (!body.Contains("{\"status\":\"OK\"}"))
            {
                throw new Exception("Tried to unsubscribe from an event but the Diver's response was not 'OK'");
            }

            if (!_listener.HasActiveHooks)
            {
                _listener.Close();
            }
        }

        public bool HookMethod(MethodBase methodBase, HarmonyPatchPosition pos, LocalHookCallback callback, List<string> parametersTypeFullNames = null, ulong instanceAddress = 0)
        {
            if (!_listener.IsOpen)
            {
                _listener.Open();
            }

            FunctionHookRequest req = new()
            {
                IP = _listener.IP.ToString(),
                Port = _listener.Port,
                TypeFullName = methodBase.DeclaringType.FullName,
                MethodName = methodBase.Name,
                HookPosition = pos.ToString(),
                ParametersTypeFullNames = parametersTypeFullNames,
                InstanceAddress = instanceAddress
            };

            var requestJsonBody = JsonConvert.SerializeObject(req);

            var resJson = SendRequest("hook_method", null, requestJsonBody);
            if (resJson.Contains("\"error\":"))
            {
                throw new Exception("Hook Method failed. Error from Diver: " + resJson);
            }
            EventRegistrationResults regRes = JsonConvert.DeserializeObject<EventRegistrationResults>(resJson);

            _listener.HookSubscribe(callback, methodBase, regRes.Token);
            // Getting back the token tells us the hook was registered successfully.
            return true;
        }
        public void UnhookMethod(LocalHookCallback callback)
        {
            int token = _listener.HookUnsubscribe(callback);

            Dictionary<string, string> queryParams;
            string body;
            queryParams = new() { };
            queryParams["token"] = token.ToString();
            body = SendRequest("unhook_method", queryParams);
            if (!body.Contains("{\"status\":\"OK\"}"))
            {
                throw new Exception("Tried to unhook a method but the Diver's response was not 'OK'");
            }

            if (!_listener.HasActiveHooks)
            {
                _listener.Close();
            }
        }

        public delegate (bool voidReturnType, ObjectOrRemoteAddress res) LocalEventCallback(ObjectOrRemoteAddress[] args, ObjectOrRemoteAddress retVal);

        public bool RegisterCustomFunction(RegisterCustomFunctionRequest request)
        {
            return RegisterCustomFunction(request, out _);
        }

        public bool RegisterCustomFunction(RegisterCustomFunctionRequest request, out TypeDump.TypeMethod methodDump)
        {
            methodDump = null;
            var requestJsonBody = JsonConvert.SerializeObject(request);
            var resJson = SendRequest("register_custom_function", null, requestJsonBody);

            try
            {
                RegisterCustomFunctionResponse response = JsonConvert.DeserializeObject<RegisterCustomFunctionResponse>(resJson, _withErrors);
                if (response?.Success == true)
                {
                    methodDump = response.RegisteredMethod;
                }
                return response?.Success ?? false;
            }
            catch (Exception ex)
            {
                throw new Exception($"Failed to register custom function. Error: {ex.Message}", ex);
            }
        }

        public void Dispose()
        {
            if (_httpClient != null)
            {
                try
                {
                    _httpClient.Dispose();
                }
                catch
                {
                }
            }
        }
    }
}
