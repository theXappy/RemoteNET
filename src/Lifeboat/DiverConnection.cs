using System.Collections.Concurrent;
using System.Net.Sockets;
using ScubaDiver.API.Protocol.SimpleHttp;

namespace Lifeboat
{
    public class DiverConnection
    {
        public TcpClient TcpClient { get; private set; }
        private readonly Task _writer;
        private readonly Task _reader;

        private BlockingCollection<HttpRequestSummary> _requests;
        private ConcurrentDictionary<string, HttpResponseSummary> _responses;
        private ConcurrentDictionary<string, AutoResetEvent> _responseReceivedEvents;

        public DiverConnection(TcpClient tcpClient)
        {
            TcpClient = tcpClient;
            _requests = new();
            _responses = new();
            _responseReceivedEvents = new ConcurrentDictionary<string, AutoResetEvent>();
            _writer = Task.Run(Writer);
            _reader = Task.Run(Reader);
        }

        private void Reader()
        {
            Console.WriteLine("[Diver Reader] Started");
            while (true)
            {
                HttpResponseSummary resp = null;
                try
                {
                    resp = SimpleHttpProtocolParser.ReadResponse(TcpClient.GetStream());
                }
                catch (IOException)
                {
                    break;
                }

                if (resp == null)
                    break;

                _responses[resp.RequestId] = resp;
                _responseReceivedEvents[resp.RequestId].Set();
            }
            Console.WriteLine("[Diver Reader] Exiting...");
        }

        private void Writer()
        {
            Console.WriteLine("[Diver Writer] Exiting...");
            foreach (HttpRequestSummary request in _requests.GetConsumingEnumerable())
            {
                SimpleHttpProtocolParser.Write(TcpClient.GetStream(), request);
            }
            Console.WriteLine("[Diver Writer] Exiting...");
        }

        public async Task<HttpResponseSummary> SendAsync(HttpRequestSummary req)
        {
            string oldId = req.RequestId;
            string newId = AllocateRequestId();
            req.RequestId = newId;

            AutoResetEvent responseEvent = new AutoResetEvent(false);
            _responseReceivedEvents[newId] = responseEvent;

            _requests.Add(req);

            await responseEvent.WaitOneAsync();

            _responseReceivedEvents.TryRemove(newId, out _);
            if (!_responses.TryRemove(newId, out HttpResponseSummary resp))
                throw new Exception("Error: Reset event signaled but response was not found");

            resp.RequestId = oldId;
            return resp;
        }

        private int nextId = 10;
        public string AllocateRequestId()
        {
            int id = Interlocked.Increment(ref nextId);
            return id.ToString();
        }

    }
}
