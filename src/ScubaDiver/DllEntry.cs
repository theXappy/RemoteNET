using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Sockets;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Win32.SafeHandles;

namespace ScubaDiver
{
    public class DllEntry
    {
        #region P/Invoke Console Spawning
        [DllImport("kernel32.dll",
            EntryPoint = "GetStdHandle",
            SetLastError = true,
            CharSet = CharSet.Auto,
            CallingConvention = CallingConvention.StdCall)]
        private static extern IntPtr GetStdHandle(int nStdHandle);
        [DllImport("kernel32.dll",
            EntryPoint = "AllocConsole",
            SetLastError = true,
            CharSet = CharSet.Auto,
            CallingConvention = CallingConvention.StdCall)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool AllocConsole();
        private const int STD_OUTPUT_HANDLE = -11;
        private const int MY_CODE_PAGE = 437;
        #endregion

        private static bool _assembliesResolverRegistered = false;

        public static Assembly AssembliesResolverFunc(object sender, ResolveEventArgs args)
        {
            string requestedAssemblyName = new AssemblyName(args.Name).Name;
            foreach (Assembly loadedAssembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                if (loadedAssembly.GetName().Name == requestedAssemblyName)
                    return loadedAssembly;
            }

            // Assembly not loaded in target, try to load from the Diver's dll files
            string folderPath = Path.GetDirectoryName(typeof(DllEntry).Assembly.Location);
            string assemblyPath = Path.Combine(folderPath, requestedAssemblyName + ".dll");
            if (!File.Exists(assemblyPath)) return null;
            Assembly assembly = Assembly.LoadFrom(assemblyPath);
            return assembly;
        }

        public static void DiverHost(object pwzArgument)
        {
            try
            {
                string config = (string)pwzArgument;
                string[] args = config.Split('~');
                ushort port = ushort.Parse(args[0]);
                bool reverseProxy = args.Any(arg => arg == "reverse");

                // Run Divers
                Task[] tasks = new Task[1];
#if NET_6
                tasks = new Task[2];
                IRequestsListener msvcListener = RnetRequestsListenerFactory.Create((ushort)(port + 2), reverseProxy);
                MsvcDiver _msvcInstance = new(msvcListener);
                tasks[1] = Task.Run(() =>
                {
                    _msvcInstance.Start();
                    _msvcInstance.WaitForExit();
                });
#endif
                IRequestsListener listener = RnetRequestsListenerFactory.Create(port, reverseProxy);
                DotNetDiver _instance = new(listener);
                tasks[0] = Task.Run(() =>
                {
                    _instance.Start();
                    _instance.WaitForExit();
                });


                Task.WaitAll(tasks);
                // Diver(s) killed (politely)
#if NET_6
                Logger.Debug("[DiverHost] DotNetDiver and MsvcDiver both finished gracefully, returning");
#else
                Logger.Debug("[DiverHost] DotNetDiver finished gracefully, returning");
#endif
            }
            catch (Exception e)
            {
                Console.WriteLine("[DiverHost] ScubaDiver crashed.");
                Console.WriteLine(e);
                Console.WriteLine("[DiverHost] Exiting entry point in 60 secs...");
                Thread.Sleep(TimeSpan.FromSeconds(60));
            }
            finally
            {
                AppDomain.CurrentDomain.AssemblyResolve -= AssembliesResolverFunc;
                Logger.Debug("[DiverHost] unhooked assemblies resolver.");
            }
        }


        public static int EntryPoint(string pwzArgument)
        {
            // UnmanagedAdapterDLL needs to call a C# function with exactly this signature.
            // So we use it to just create a diver, and run the Start func (blocking)

            // DotNetDiver needs some assemblies which might not be loaded in the target process
            // so starting off with registering an assembly resolver to the DotNetDiver's dll's directory
            if (!_assembliesResolverRegistered)
            {
                AppDomain.CurrentDomain.AssemblyResolve += AssembliesResolverFunc;
                Logger.Debug("[EntryPoint] Loaded + hooked assemblies resolver.");
                _assembliesResolverRegistered = true;
            }

            if (Logger.DebugInRelease.Value &&
                !Debugger.IsAttached)
            {
                // If we need to log and a debugger isn't attached then we can't use
                // the Debug.Write(Line) method. We need a console, which the app might not current have.
                // (For example, if it's a GUI application)
                // So here we are trying to allocate a console & redirect STDOUT to it.
                if (AllocConsole())
                {
                    IntPtr stdHandle = GetStdHandle(STD_OUTPUT_HANDLE);
                    SafeFileHandle safeFileHandle = new SafeFileHandle(stdHandle, true);
                    FileStream fileStream = new FileStream(safeFileHandle, FileAccess.Write);
                    Encoding encoding = Encoding.ASCII;
                    StreamWriter standardOutput = new StreamWriter(fileStream, encoding);
                    standardOutput.AutoFlush = true;
                    Console.SetOut(standardOutput);
                }
            }

            ParameterizedThreadStart func = DiverHost;
            Thread diverHostThread = new Thread(func);
            diverHostThread.Start(pwzArgument);

            Logger.Debug("[EntryPoint] Returning");
            return 0;
        }
    }
}
