using System;
using System.IO;
using System.Reflection;
using System.Threading;

namespace ScubaDiver
{
    public class DllEntry
    {
        public static Assembly AssembliesResolverFunc(object sender, ResolveEventArgs args)
        {
            string folderPath = Path.GetDirectoryName(typeof(DllEntry).Assembly.Location);
            string assemblyPath = Path.Combine(folderPath, new AssemblyName(args.Name).Name + ".dll");
            if (!File.Exists(assemblyPath)) return null;
            Assembly assembly = Assembly.LoadFrom(assemblyPath);
            return assembly;
        }


        public static int EntryPoint(string pwzArgument)
        {
            // UnmanagedAdapterDLL needs to call a C# function with exactly this signature.
            // So we use it to just create a diver, and run the Dive func (blocking)

            // Diver needs some assemblies which might not be loaded in the target process
            // so starting off with registering an assembly resolver to the Diver's dll's directory
            AppDomain.CurrentDomain.AssemblyResolve += AssembliesResolverFunc;
            Logger.Debug("[Diver] Loaded + hooked assemblies resolver.");

            try
            {
                Diver _instance = new Diver();
                ushort port = ushort.Parse(pwzArgument);
                _instance.Dive(port);

                // Diver killed (politely)
                Logger.Debug("[Diver] Diver finished gracefully, Entry point returning");
                return 0;
            }
            catch (Exception e)
            {
                Console.WriteLine("[Diver] ScubaDiver crashed.");
                Console.WriteLine(e);
                Console.WriteLine("[Diver] Exiting entry point in 60 secs...");
                Thread.Sleep(TimeSpan.FromSeconds(60));
                return 1;
            }
            finally
            {
                AppDomain.CurrentDomain.AssemblyResolve -= AssembliesResolverFunc;
                Logger.Debug("[Diver] unhooked assemblies resolver.");
            }
        }
    }
}
