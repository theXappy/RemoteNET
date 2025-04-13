using ScubaDiver;
using System;

namespace StaticAnalyzer
{
    class Program
    {
        static void Main(string[] args)
        {
            Console.WriteLine("Creating HTTP Listener for MANAGED");
            var listener = new HttpRequestsListener(System.Diagnostics.Process.GetCurrentProcess().Id);
            var diver = new DotNetDiver(listener);
            Console.WriteLine("Creating HTTP Listener for UNMANAGED");
            var listenerUnmanaged= new HttpRequestsListener(System.Diagnostics.Process.GetCurrentProcess().Id + 2);
            var diverUnmanaged = new MsvcDiver(listenerUnmanaged)   ;
            diver.Start();
            diverUnmanaged.Start();

            Console.WriteLine("StaticAnalyzer running. Press any key to exit.");
            Console.ReadKey();
            diver.Dispose();
        }
    }
}
