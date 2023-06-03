using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
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
                ushort port = (ushort)Process.GetCurrentProcess().Id;
                DotNetDiver dive = new(new RnetRequestsListener(port));
                dive.Start();
                dive.WaitForExit();
                Console.WriteLine("[+] Diver Task: Diver Exited");
            });
            Console.WriteLine("[+] Launched Diver Task");

            Console.WriteLine("[-] Launching MSVC Diver Task");
            Task diverTask2 = Task.Run(() =>
            {
                Console.WriteLine("[+] Diver Task: Started");
                ushort port = (ushort)Process.GetCurrentProcess().Id;
                MsvcDiver dive = new(new RnetRequestsListener(port + 2));
                dive.Start();
                dive.WaitForExit();
                Console.WriteLine("[+] Diver Task: Diver Exited");
            });
            Console.WriteLine("[+] Launched Diver Task");

            Console.WriteLine("[-] Waiting for Q or Diver exit");
            List<Task> tasks = new List<Task>() { diverTask, diverTask2 };
            while (true)
            {
                if (Task.WaitAny(tasks.ToArray(), TimeSpan.FromMilliseconds(100)) != -1)
                {
                    Console.WriteLine("[+] A diver exited");
                    tasks = tasks.Where(task => !task.IsCompleted && !task.IsCanceled).ToList();
                }
                if(!tasks.Any())
                    break;
                    
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
