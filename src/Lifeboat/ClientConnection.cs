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
            Console.WriteLine("[Client Worker] Started");
            while (true)
            {
                try
                {
                    HttpRequestSummary req = SimpleHttpProtocolParser.ReadRequest(_client);
                    if (req == null)
                    {
                        break;
                    }

                    _diver.SendAsync(req).ContinueWith(HandleResponse);
                }
                catch (IOException)
                {
                    // Probably just a client disconnect
                    break;
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[Client Worker] Error in worker loop. Ex: {ex}");
                    break;
                }
            }

            Console.WriteLine($"[Client Worker] Closing client");
            try
            {
                _client.Close();
            }
            catch (Exception ex)
            {
            }
            Alive = false;
            Console.WriteLine("[Client Worker] Finished");
        }

        private void HandleResponse(Task<HttpResponseSummary> task)
        {
            lock (_writeLock)
            {
                try
                {
                    SimpleHttpProtocolParser.WriteResponse(_client, task.Result);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[ClientConnection] Error while writing a response to a client. Ex: {ex}");
                }
            }
        }
    }
}
