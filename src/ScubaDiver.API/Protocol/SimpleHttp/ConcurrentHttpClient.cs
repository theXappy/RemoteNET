using System;
using System.Collections.Concurrent;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

namespace ScubaDiver.API.Protocol.SimpleHttp
{
    public class ConcurrentHttpClient : IDisposable
    {
        private TcpClient _client;
        private NetworkStream _netStream;
        private Task _reader;
        private Task _writer;
        private ConcurrentDictionary<string, AutoResetEvent> _autoResetEvents;
        private ConcurrentDictionary<string, HttpResponseSummary> _responses;
        private BlockingCollection<HttpRequestSummary> _requests;
        private int _nextId;

        public ConcurrentHttpClient(TcpClient c)
        {
            _client = c;
            _netStream = c.GetStream();

            _autoResetEvents = new ConcurrentDictionary<string, AutoResetEvent>();
            _responses = new ConcurrentDictionary<string, HttpResponseSummary>();
            _requests = new BlockingCollection<HttpRequestSummary>();
            _nextId = 5;

            _reader = Task.Run(DoRead);
            _writer = Task.Run(DoWrite);
        }

        private void DoWrite()
        {
            foreach (HttpRequestSummary httpRequestSummary in _requests.GetConsumingEnumerable())
            {
                SimpleHttpProtocolParser.Write(_client, httpRequestSummary);
            }
        }

        private void DoRead()
        {
            while (true)
            {
                HttpResponseSummary resp = null;
                try
                {
                    resp = SimpleHttpProtocolParser.Read<HttpResponseSummary>(_client);
                }
                catch
                {
                    break;
                }
                if (resp == null)
                    break;

                if (!resp.OtherHeaders.TryGetValue("requestId", out string id))
                {
                    throw new Exception("Responses reader: No request ID found in HTTP response! Response: " + resp);
                }

                if (!_autoResetEvents.TryRemove(id, out AutoResetEvent? are))
                {
                    throw new Exception($"Responses reader: No AutoResetEvent found for requestID: {id}. Response: " + resp);
                }

                if (_responses.TryGetValue(id, out var existing))
                {
                    throw new Exception(
                        $"Dulpicate response for same request ID: {id}. Existing Response: {existing}, New Response: {resp}");
                }

                _responses[id] = resp;
                are.Set();
            }

            // Signal all reset event to allow waiting Sender threads to realize the connection broke.
            foreach (AutoResetEvent item in _autoResetEvents.Values)
            {
                item.Set();
            }
        }

        public HttpResponseSummary Send(HttpRequestSummary request)
        {
            AutoResetEvent are = new AutoResetEvent(false);
            string myId = Interlocked.Increment(ref _nextId).ToString();
            request.QueryString.Add("requestId", myId);
            _autoResetEvents[myId] = are;

            // Send
            _requests.Add(request);

            // Wait for response
            are.WaitOne();
            if (!_responses.TryRemove(myId, out HttpResponseSummary val))
                throw new Exception("AutoResetEvent was signaled but a resposnes wasn't found in the responses dict.");

            return val;
        }

        public void Dispose()
        {
            try { _client.Dispose(); } catch { }
            try { _netStream.Dispose(); } catch { }
            try { _reader.Dispose(); } catch { }
            try { _writer.Dispose(); } catch { }
            try { _requests.CompleteAdding(); } catch { }
            try { _requests.Dispose(); } catch { }
        }
    }
}