using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Runtime.Serialization;
using System.Text;
using System.Threading.Tasks;
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

        public DiverCommunicator(string hostname, int port)
        {
            _hostname = hostname;
            _port = port;
        }

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
                { "name", type }
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

        public ObjectDump DumpObject(ulong address, bool pinObject = false)
        {
            Dictionary<string, string> queryParams = new()
            {
                { "address", address.ToString() },
                { "pinRequest", pinObject.ToString() }
            };
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
    }
}
