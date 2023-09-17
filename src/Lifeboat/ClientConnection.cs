using System.Net.Sockets;
using ScubaDiver.API.Protocol.SimpleHttp;

namespace Lifeboat
{
    public class ClientConnection
    {
        public bool Alive { get; private set; }
        private TcpClient _client;
        private DiverConnection _diver;

        private object _writeLock;
        private Task _worker;

        public ClientConnection(TcpClient client, DiverConnection diver)
        {
            Alive = true;
            _client = client;
            _diver = diver;

            _writeLock = new object();
            _worker = Task.Run(Work);
        }

        private void Work()
        {
            while (true)
            {
                try
                {
                    Console.WriteLine($"[ClientConnection][@@@] Reading next request...");
                    HttpRequestSummary req = SimpleHttpProtocolParser.ReadRequest(_client);
                    if (req == null)
                    {
                        Console.WriteLine($"[ClientConnection] Parser returned NULL for request. Stopping loop.");
                        break;
                    }
                    Console.WriteLine($"[ClientConnection][@@@] Next request found! URL: {req.Url} , ID: {req.RequestId}");
                    Console.WriteLine($"[ClientConnection][@@@] Sending request to diver connection... ");
                    _diver.SendAsync(req).ContinueWith(HandleResponse);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[ClientConnection] Error in worker loop. Ex: {ex}");
                    break;
                }
            }

            Console.WriteLine($"[ClientConnection] Closing client");
            try
            {
                _client.Close();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[ClientConnection] Error closing client. Ex: {ex}");
            }

            Alive = false;
        }

        private void HandleResponse(Task<HttpResponseSummary> task)
        {
            Console.WriteLine($"[ClientConnection][@@@] Got back response from diver! Request ID: {task.Result.RequestId}");
            lock (_writeLock)
            {
                Console.WriteLine($"[ClientConnection][@@@] Acquired Writer Lock! Request ID: {task.Result.RequestId}");
                try
                {
                    Console.WriteLine($"[ClientConnection][@@@] Sending response to client... Request ID: {task.Result.RequestId}");
                    SimpleHttpProtocolParser.WriteResponse(_client, task.Result);
                    Console.WriteLine($"[ClientConnection][@@@] Sent response to client. Request ID: {task.Result.RequestId}");
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[ClientConnection] Error in writer. Ex: {ex}");
                }
            }
        }
    }
}
