using System;
using System.Diagnostics;
using System.Threading.Tasks;
using ScubaDiver;

namespace DebuggableDummy
{
    internal class Program
    {
        static void Main(string[] args)
        {
            Console.WriteLine("[+] Debuggable Dummy starting");
            Console.WriteLine("[-] Launching .NET Diver Task");
            Task diverTask = Task.Run(() =>
            {
                Console.WriteLine("[+] Diver Task: Started");
                DotNetDiver dive = new();
                ushort port = (ushort)Process.GetCurrentProcess().Id;
                dive.Start(port);
                Console.WriteLine("[+] Diver Task: Diver Exited");
            });
            Console.WriteLine("[+] Launched Diver Task");

            Console.WriteLine("[-] Launching MSVC Diver Task");
            Task diverTask2 = Task.Run(() =>
            {
                Console.WriteLine("[+] Diver Task: Started");
                MsvcDiver dive = new();
                ushort port = (ushort)Process.GetCurrentProcess().Id;
                dive.Start((ushort)(port + 2));
                Console.WriteLine("[+] Diver Task: Diver Exited");
            });
            Console.WriteLine("[+] Launched Diver Task");

            Console.WriteLine("[-] Waiting for Q or Diver exit");
            while (true)
            {
                if (diverTask.Wait(TimeSpan.FromMilliseconds(100)))
                {
                    Console.WriteLine("[+] Diver exited");
                    break;
                }

                while (Console.KeyAvailable)
                {
                    if (Console.ReadKey().Key == ConsoleKey.Q)
                    {
                        Console.WriteLine("[+] User requested to exit");
                        break;
                    }
                }
            }
            Console.WriteLine("[+] Exiting");
        }

    }
}
