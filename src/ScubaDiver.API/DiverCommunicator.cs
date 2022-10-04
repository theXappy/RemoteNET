using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using System.Web;
using ScubaDiver.API.Dumps;

namespace ScubaDiver.API
{

    /// <summary>
    /// Communicates with a diver in a remote process
    /// </summary>
    public class DiverCommunicator
    {
        readonly object _withErrors = NewtonsoftProxy.JsonSerializerSettingsWithErrors;

        private readonly string _hostname;
        private readonly int _port;

        private int? _process_id = null;
        private CallbacksListener _listener;

        public DiverCommunicator(string hostname, int port)
        {
            _hostname = hostname;
            _port = port;
            _listener = new CallbacksListener(this, _port + 1);
        }
        public DiverCommunicator(IPAddress ipa, int port) : this(ipa.ToString(), port) { }
        public DiverCommunicator(IPEndPoint ipe) : this(ipe.Address, ipe.Port) { }

        private string SendRequest(string path, Dictionary<string, string> queryParams = null, string jsonBody = null)
        {
            queryParams ??= new();

            HttpClient c = new();
            string query = "";
            bool firstParam = true;
            foreach (KeyValuePair<string, string> kvp in queryParams)
            {
                query += firstParam ? "?" : "&";
                query += $"{kvp.Key}={kvp.Value}";
                firstParam = false;
            }

            string url = $"http://{_hostname}:{_port}/{path}" + query;
            HttpRequestMessage msg;
            if (jsonBody == null)
            {
                msg = new HttpRequestMessage(HttpMethod.Get, url);
            }
            else
            {
                msg = new HttpRequestMessage(HttpMethod.Post, url);
                msg.Content = new StringContent(jsonBody, Encoding.UTF8,
                                    "application/json");
            }

            HttpResponseMessage res = c.SendAsync(msg).Result;
            string body = res.Content.ReadAsStringAsync().Result;
            c.Dispose();
            if (body.StartsWith("{\"error\":", StringComparison.InvariantCultureIgnoreCase))
            {
                // Try to parse generic error:
                DiverError errMessage = null;
                try
                {
                    errMessage = JsonConvert.DeserializeObject<DiverError>(body, _withErrors);
                }
                catch
                {
                    // Let someone else handle this...
                    throw;
                }
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

        internal bool RegisterCallbackEndpoint(string ip, int localHttpPort)
        {
            Dictionary<string, string> queryParams = new();
            queryParams["ip"] = ip;
            queryParams["port"] = localHttpPort.ToString();
            string body = SendRequest("register_callbacks_ep", queryParams);
            if (!body.Contains("\"status\":\"OK\""))
            {
                throw new Exception("Local HTTP server created but informing the remote Diver resulted in an error. " +
                    "Raw response: " + body);
            }
            return true;
        }

        /// <summary>
        /// Dumps the heap of the remote process
        /// </summary>
        /// <param name="typeFilter">TypeFullName filter of objects to get from the heap. Support leading/trailing wildcard (*). NULL returns all objects</param>
        /// <returns></returns>
        public HeapDump DumpHeap(string typeFilter = null)
        {
            Dictionary<string, string> queryParams = new() { };
            if (typeFilter != null)
            {
                queryParams["type_filter"] = typeFilter;
            }
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

        public TypesDump DumpTypes(string assembly)
        {
            Dictionary<string, string> queryParams = new() { };
            queryParams["assembly"] = assembly;

            string body = SendRequest("types", queryParams);
            TypesDump? results = JsonConvert.DeserializeObject<TypesDump>(body, _withErrors);

            return results;
        }

        public TypeDump DumpType(string type, string assembly = null)
        {
            TypeDumpRequest dumpRequest = new TypeDumpRequest()
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

        public ObjectDump DumpObject(ulong address, bool pinObject = false, int? hashcode = null)
        {
            Dictionary<string, string> queryParams = new()
            {
                { "address", address.ToString() },
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
                string body = SendRequest("register_client", new Dictionary<string, string> { { "process_id", _process_id.Value.ToString() } });
                return body.Contains("{\"status\":\"OK'\"}");
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

        public ObjectOrRemoteAddress GetItem(ulong token, int key)
        {
            Dictionary<string, string> queryParams = new()
            {
                { "address", token.ToString() },
                { "pinRequest", "true" },
                { "index", key.ToString() }
            };
            string body = SendRequest("get_item", queryParams);
            if (body.Contains("\"error\":"))
            {
                throw new Exception("Diver failed to dump item of array object. Error: " + body);
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

            queryParams = new() { };
            queryParams["address"] = targetAddr.ToString();
            queryParams["event"] = eventName;
            body = SendRequest("event_subscribe", queryParams);
            EventRegistrationResults regRes = JsonConvert.DeserializeObject<EventRegistrationResults>(body);
            _listener.EventSubscribe(callback, regRes.Token);
        }

        public void EventUnsubscribe(LocalEventCallback callback)
        {
            int token = _listener.EventUnsubscribe(callback);

            Dictionary<string, string> queryParams = new() { };
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

        public bool HookMethod(string type, string methodName, HarmonyPatchPosition pos, LocalHookCallback callback, List<string> parametersTypeFullNames = null)
        {
            Dictionary<string, string> queryParams;
            string body;
            if (!_listener.IsOpen)
            {
                _listener.Open();
            }

            FunctionHookRequest req = new()
            {
                TypeFullName = type,
                MethodName = methodName,
                HookPosition = pos.ToString(),
                ParametersTypeFullNames = parametersTypeFullNames
            };

            var requestJsonBody = JsonConvert.SerializeObject(req);

            var resJson = SendRequest("hook_method", null, requestJsonBody);
            if (resJson.Contains("\"error\":"))
            {
                throw new Exception("Hook Method failed. Error from Diver: " + resJson);
            }
            EventRegistrationResults regRes = JsonConvert.DeserializeObject<EventRegistrationResults>(resJson);

            _listener.HookSubscribe(callback, regRes.Token);
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

        public delegate (bool voidReturnType, ObjectOrRemoteAddress res) LocalEventCallback(ObjectOrRemoteAddress[] args);

    }
}
