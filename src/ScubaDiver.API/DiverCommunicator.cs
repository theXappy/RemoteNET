using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Runtime.Serialization;
using System.Text;
using System.Threading.Tasks;
using System.Web;
using Microsoft.Diagnostics.Runtime;
using Newtonsoft.Json;
using ScubaDiver.API.Dumps;

namespace ScubaDiver.API
{
    /// <summary>
    /// Communicates with a diver in a remote process
    /// </summary>
    public class DiverCommunicator
    {
        JsonSerializerSettings _withErrors = new()
        {
            MissingMemberHandling = MissingMemberHandling.Error
        };

        private string _hostname;
        private int _port;

        private HttpListener _listener = null;

        public DiverCommunicator(string hostname, int port)
        {
            _hostname = hostname;
            _port = port;
        }
        public DiverCommunicator(IPAddress ipa, int port) : this(ipa.ToString(), port) { }
        public DiverCommunicator(IPEndPoint ipe) : this(ipe.Address, ipe.Port) { }

        private string SendRequest(string path, Dictionary<string, string> queryParams = null, string jsonBody = null)
        {
            queryParams ??= new();

            HttpClient c = new HttpClient();
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
                msg.Content = new StringContent(jsonBody);
            }

            HttpResponseMessage res = c.SendAsync(msg).Result;
            string body = res.Content.ReadAsStringAsync().Result;
            return body;
        }

        public bool KillDiver()
        {
            string body = SendRequest("die");
            return body?.Contains("Goodbye") ?? false;
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

        public TypeDump DumpType(string type, string assembly = null)
        {
            Dictionary<string, string> queryParams = new()
            {
                { "name", HttpUtility.UrlEncode(type) }
            };
            if (assembly != null)
            {
                queryParams["assembly"] = assembly;
            }

            string body = SendRequest("type", queryParams);
            TypeDump typeDump;
            try
            {
                typeDump = JsonConvert.DeserializeObject<TypeDump>(body, _withErrors);
            }
            catch
            {
                typeDump = null;
            }
            return typeDump;
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
            params ObjectOrRemoteAddress[] args)
        {
            InvocationRequest invocReq = new InvocationRequest()
            {
                ObjAddress = targetAddr,
                TypeFullName = targetTypeFullName,
                MethodName = methodName,
                Parameters = args.ToList()
            };
            var requestJsonBody = JsonConvert.SerializeObject(invocReq);

            var resJson = SendRequest("invoke", null, requestJsonBody);

            InvocationResults res = JsonConvert.DeserializeObject<InvocationResults>(resJson, _withErrors);
            return res;
        }

        public InvocationResults InvokeStaticMethod(string targetTypeFullName, string methodName,
            params ObjectOrRemoteAddress[] args) => InvokeMethod(0, targetTypeFullName, methodName, args);

        public ObjectDump CreateObject(string typeFullName, ObjectOrRemoteAddress[] args)
        {
            var ctorInvocReq = new CtorInvocationRequest()
            {
                TypeFullName = typeFullName,
                Parameters = args.ToList()
            };
            var requestJsonBody = JsonConvert.SerializeObject(ctorInvocReq);

            var resJson = SendRequest("create_object", null, requestJsonBody);
            ObjectDump res = JsonConvert.DeserializeObject<ObjectDump>(resJson, _withErrors);
            return res;
        }

        public InvocationResults SetField(ulong targetAddr, string targetTypeFullName, string fieldName, ObjectOrRemoteAddress newValue)
        {
            FieldSetRequest invocReq = new FieldSetRequest()
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
            FieldGetRequest invocReq = new FieldGetRequest()
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
            Console.WriteLine($"[Communicator]EventSubscribe target: {targetAddr}, event: {eventName}, callback: {callback}");
            Dictionary<string, string> queryParams;
            string body;
            if (this._listener == null)
            {
                Console.WriteLine("[Communicator] Event subscription seen but no local listener exists. Creating now.");
                // Need to create HTTP listener and send the Diver it's info
                int localHttpPort = this._port + 1;
                string ip = "127.0.0.1";
                _listener = new HttpListener();
                string listeningUrl = $"http://{ip}:{localHttpPort}/";
                _listener.Prefixes.Add(listeningUrl);
                _listener.Start();
                Task.Run(()=>Dispatcher(_listener));

                queryParams = new();
                queryParams["ip"] = ip;
                queryParams["port"] = localHttpPort.ToString();
                body = SendRequest("register_callbacks_ep", queryParams);
                if (!body.Contains("\"status\":\"OK\""))
                {
                    throw new Exception("Local HTTP server created but informing the remote Diver resulted in an error. " +
                        "Raw response: " + body);
                }
            }

            queryParams = new() { };
            queryParams["address"] = targetAddr.ToString();
            queryParams["event"] = eventName;
            body = SendRequest("event_subscribe", queryParams);
            EventRegistrationResults regRes = JsonConvert.DeserializeObject<EventRegistrationResults>(body);

            _tokensToEventHandlers[regRes.Token] = callback;
            _eventHandlersToToken[callback] = regRes.Token;
        }
        public void EventUnsubscribe(LocalEventCallback callback)
        {
            Console.WriteLine($"[Communicator]EventUnsubscribe callback: {callback}");
            Dictionary<string, string> queryParams;
            string body;

            if (_eventHandlersToToken.TryGetValue(callback, out int token))
            {

                queryParams = new() { };
                queryParams["token"] = token.ToString();
                Console.WriteLine($"[Communicator]EventUnsubscribe Sending HTTP request to event_unsubscribe");
                body = SendRequest("event_unsubscribe", queryParams);
                Console.WriteLine($"[Communicator]EventUnsubscribe Got resp from event_unsubscribe. Response: {body}");
                if(!body.Contains("{\"status\":\"OK\"}"))
                {
                    throw new Exception("Tried to unsubscribe from an event but the Diver's response was not 'OK'");
                }

                _tokensToEventHandlers.Remove(token);
                _eventHandlersToToken.Remove(callback);
            }
            else
            {
                Console.WriteLine($"[Communicator]EventUnsubscribe TryGetValue failed :(((((((((((((((");
            }
        }

        public delegate (bool voidReturnType, ObjectOrRemoteAddress res) LocalEventCallback(ObjectOrRemoteAddress[] args);

        private Dictionary<int, LocalEventCallback> _tokensToEventHandlers = new();
        private Dictionary<LocalEventCallback, int> _eventHandlersToToken = new();

        private void Dispatcher(HttpListener listener)
        {
            while (true)
            {
                var requestContext = listener.GetContext();
                HttpListenerRequest request = requestContext.Request;

                var response = requestContext.Response;
                string body = null;
                if (request.Url.AbsolutePath == "/invoke_callback")
                {
                    using (StreamReader sr = new StreamReader(request.InputStream))
                    {
                        body = sr.ReadToEnd();
                    }
                    CallbackInvocationRequest res = JsonConvert.DeserializeObject<CallbackInvocationRequest>(body, _withErrors);
                    if(_tokensToEventHandlers.TryGetValue(res.Token, out LocalEventCallback callbackFunction))
                    {
                        (bool voidReturnType, ObjectOrRemoteAddress callbackRes) = callbackFunction(res.Parameters.ToArray());
                        // TODO: Read action results and return them

                        InvocationResults ir = new InvocationResults()
                        { 
                            VoidReturnType = voidReturnType
                        };
                        if(!voidReturnType)
                        {
                            ir.ReturnedObjectOrAddress = callbackRes;
                        }
                        
                        body = JsonConvert.SerializeObject(ir);
                    }
                    else
                    {
                        Console.WriteLine($"[WARN] Diver tried to trigger a callback with unknown token value: {res.Token}");
                        body = "{\"error\":\"Unknown Token\"}"; ;
                    }
                }
                else
                {
                    body = "{\"error\":\"Unknown Command\"}";
                }

                byte[] buffer = System.Text.Encoding.UTF8.GetBytes(body);
                // Get a response stream and write the response to it.
                response.ContentLength64 = buffer.Length;
                response.ContentType = "application/json";
                System.IO.Stream output = response.OutputStream;
                output.Write(buffer, 0, buffer.Length);
                // You must close the output stream.
                output.Close();
            }
        }

    }
}
