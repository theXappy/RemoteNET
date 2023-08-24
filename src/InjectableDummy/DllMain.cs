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
            Console.WriteLine("[InjectableDummy] .NET runtime successfully hosted.");
            string runtimeDesc = RuntimeInformation.FrameworkDescription;
            Console.WriteLine("[InjectableDummy] Runtime description: " + runtimeDesc);
            return 0;
        }
    }
}