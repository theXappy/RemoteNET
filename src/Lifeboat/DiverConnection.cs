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
            Console.WriteLine("[Reader] Started");
            while (true)
            {
                Console.WriteLine("[Reader] > Another loop iteration!");
                Console.WriteLine("[Reader] Reading another response from Diver...");
                HttpResponseSummary resp = SimpleHttpProtocolParser.ReadResponse(TcpClient);
                Console.WriteLine($"[Reader] Reading response Finished! Is it null? {resp == null}");

                Console.WriteLine($"[Reader] Forwaring the response back to it's owner...");
                _responses[resp.RequestId] = resp;
                Console.WriteLine($"[Reader] Signaling owner's AutoResetEvent...");
                _responseReceivedEvents[resp.RequestId].Set();
                Console.WriteLine($"[Reader] > Finished another iteration!");
            }
        }

        private void Writer()
        {
            Console.WriteLine("[Writer] Started. Reading from blocking collection...");
            foreach (HttpRequestSummary request in _requests.GetConsumingEnumerable())
            {
                Console.WriteLine("[Writer] NEW request found in blocking collection!~!~!~");
                Console.WriteLine("[Writer] writing to diver's TCP connection...");
                SimpleHttpProtocolParser.Write(TcpClient, request);
                Console.WriteLine("[Writer] Done writing to diver!");
            }

            Console.WriteLine("[Writer] Enumeration Ended ?????????");
        }

        public async Task<HttpResponseSummary> SendAsync(HttpRequestSummary req)
        {
            Console.WriteLine("[SendAsync] Entered");
            Console.WriteLine($"[SendAsync] req == null ? {req == null}");
            string oldId = req.RequestId;
            Console.WriteLine($"[SendAsync] Original Request ID: {oldId}");
            Console.WriteLine("[SendAsync] Allocating ID");
            string newId = AllocateRequestId();
            Console.WriteLine($"[SendAsync] Allocated! ID: {newId}");
            Console.WriteLine($"[SendAsync] Setting ID to req.");
            req.RequestId = newId;

            Console.WriteLine($"[SendAsync] Creating new AutoResetEvent");
            AutoResetEvent responseEvent = new AutoResetEvent(false);
            Console.WriteLine($"[SendAsync] Adding new AutoResetEvent to dict");
            _responseReceivedEvents[newId] = responseEvent;
            Console.WriteLine($"[SendAsync] AutoResetEvent created and added to dictionary");

            Console.WriteLine("[SendAsync] Inserting request to _requests collection");
            _requests.Add(req);
            Console.WriteLine("[SendAsync] Done adding to collection");

            Console.WriteLine("[SendAsync] waiting on AutoResetEvent...");
            await responseEvent.WaitOneAsync();
            Console.WriteLine("[SendAsync] AutoResetEvent SIGNALED!");

            Console.WriteLine("[SendAsync] Reading response left by Reader...");
            _responseReceivedEvents.TryRemove(newId, out _);
            if (!_responses.TryRemove(newId, out HttpResponseSummary resp))
                throw new Exception("Error: Reset event signaled but response was not found");
            Console.WriteLine($"[SendAsync] Response found! Request ID before fixing: {resp.RequestId}");

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

    public static class Extensions
    {
        public static Task WaitOneAsync(this WaitHandle waitHandle)
        {
            if (waitHandle == null)
                throw new ArgumentNullException("waitHandle");

            var tcs = new TaskCompletionSource<bool>();
            var rwh = ThreadPool.RegisterWaitForSingleObject(waitHandle,
                delegate { tcs.TrySetResult(true); }, null, -1, true);
            var t = tcs.Task;
            t.ContinueWith((antecedent) => rwh.Unregister(null));
            return t;
        }
    }
}
