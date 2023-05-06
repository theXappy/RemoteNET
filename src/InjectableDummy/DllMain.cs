using System;
using System.Runtime.InteropServices;
using System.Threading;

namespace InjectableDummy
{
    public class DllMain
    {
        [DllImport("kernel32")]
        public static extern bool AllocConsole();

        [UnmanagedCallersOnly(EntryPoint = "Main")]
        public static int Main(nuint data)
        {
            AllocConsole();
            new Thread
            (
                () =>
                {
                    try
                    {
                        Console.WriteLine("WIN!");
                    }
                    catch (Exception e)
                    {
                        Console.WriteLine(e.ToString());
                    }
                }
            ).Start();
            return 0;
        }
    }
}