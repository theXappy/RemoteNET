using System;
using System.Collections.Generic;
using System.Diagnostics;
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
    /// The reverse communicator is used by the Diver to communicate back with it's controller regarding callbacks invocations
    /// </summary>
    public class ReverseCommunicator
    {
        JsonSerializerSettings _withErrors = new()
        {
            MissingMemberHandling = MissingMemberHandling.Error
        };

        private string _hostname;
        private int _port;

        public ReverseCommunicator(string hostname, int port)
        {
            _hostname = hostname;
            _port = port;
        }
        public ReverseCommunicator(IPAddress ipa, int port) : this(ipa.ToString(), port) {}
        public ReverseCommunicator(IPEndPoint ipe) : this(ipe.Address, ipe.Port) {}

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

        public InvocationResults InvokeCallback(int token,
            params ObjectOrRemoteAddress[] args)
        {
            CallbackInvocationRequest invocReq = new CallbackInvocationRequest()
            {
                Token = token,
                Parameters = args.ToList()
            };
            var requestJsonBody = JsonConvert.SerializeObject(invocReq);

            var resJson = SendRequest("invoke_callback", null, requestJsonBody);

            if(resJson.Contains("\"error\":"))
            {
                return null;
            }

            InvocationResults res = JsonConvert.DeserializeObject<InvocationResults>(resJson, _withErrors);
            return res;
        }
    }
}
