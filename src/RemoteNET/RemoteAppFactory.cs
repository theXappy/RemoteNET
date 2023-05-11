using System;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Threading;
using InjectableDotNetHost.Injector;
using RemoteNET.Internal;
using RemoteNET.Internal.Extensions;
using RemoteNET.Internal.Utils;
using RemoteNET.Properties;
using RemoteNET.Utils;
using ScubaDiver.API;

namespace RemoteNET
{
    public static class RemoteAppFactory
    {
        public static RemoteApp Connect(string target, RuntimeType runtime)
        {
            try
            {
                return Connect(ProcessHelper.GetSingleRoot(target), runtime);
            }
            catch (TooManyProcessesException tooManyProcsEx)
            {
                throw new TooManyProcessesException($"{tooManyProcsEx.Message}\n" +
                                                    $"You can also get the right {typeof(System.Diagnostics.Process).FullName} object yourself and use the " +
                                                    $"overload with the {typeof(System.Diagnostics.Process).FullName} parameter to avoid this issue.", tooManyProcsEx.Matches);
            }
        }

        /// <summary>
        /// Creates a new provider.
        /// </summary>
        /// <param name="target">Process to create the provider for</param>
        /// <returns>A provider for the given process</returns>
        public static RemoteApp Connect(Process target, RuntimeType runtime)
        {
            // TODO: If target is our own process run a local Diver without DLL injections

            //
            // First Try: Use discovery to check for existing diver
            //

            // To make the Diver's port predictable even when re-attaching we'll derive it from the PID:
            ushort diverPort = (ushort)target.Id;
            // TODO: Make it configurable

            DiverState status = DiverDiscovery.QueryStatus(target);

            switch (status)
            {
                case DiverState.Alive:
                    // Everything's fine, we can continue with the existing diver
                    break;
                case DiverState.Corpse:
                    throw new Exception("Failed to connect to remote app. It seems like the diver had already been injected but it is not responding to HTTP requests.\n" +
                                        "It's suggested to restart the target app and retry.");
                case DiverState.NoDiver:
                    InjectDiver(target, diverPort);
                    break;
            }

            // Now register our program as a "client" of the diver
            string diverAddr = "127.0.0.1";

            switch (runtime)
            {
                case RuntimeType.Managed:
                    DiverCommunicator managedCom = new DiverCommunicator(diverAddr, diverPort);
                    bool registeredManaged = managedCom.RegisterClient();
                    if (registeredManaged)
                        return new ManagedRemoteApp(target, managedCom);
                    break;
                case RuntimeType.Unmanaged:
                    DiverCommunicator unmanagedCom = new DiverCommunicator(diverAddr, diverPort + 2);
                    bool registeredUnmanaged = unmanagedCom.RegisterClient();
                    if (!registeredUnmanaged)
                        return new UnmanagedRemoteApp(target, unmanagedCom);
                    break;
                case RuntimeType.Unknown:
                default:
                    throw new ArgumentOutOfRangeException(nameof(runtime), runtime, null);
            }

            throw new Exception("Registering our current app as a client in the Diver failed.");
        }

        private static void InjectDiver(Process target, ushort diverPort)
        {
            //
            // Second Try: Inject DLL, assuming not injected yet
            //

            // Determine if we are dealing with .NET Framework or .NET Core
            string targetDotNetVer = target.GetSupportedTargetFramework();

            // Not injected yet, Injecting adapter now (which should load the Diver)
            // Get different injection kit (for .NET framework or .NET core & x86 or x64)
            var res = GetInjectionToolkit(target, targetDotNetVer);
            string remoteNetAppDataDir = res.RemoteNetAppDataDir;
            string injectorPath = res.InjectorPath;
            string scubaDiverDllPath = res.ScubaDiverDllPath;
            string injectableDummy = res.InjectableDummyPath;

            // If we have a native target, We try to host a .NET Core runtime inside.
            if (targetDotNetVer == "native")
            {
                DotNetHostInjector hostInjector = new DotNetHostInjector(new DotNetHostInjectorOptions());
                var results = hostInjector.Inject(target, injectableDummy, "InjectableDummy.DllMain, InjectableDummy");
                Debug.WriteLine("hostInjector.Inject RESULTS: " + results);
            }

            string adapterExecutionArg = string.Join("*", scubaDiverDllPath,
                "ScubaDiver.DllEntry",
                "EntryPoint",
                diverPort.ToString(),
                targetDotNetVer.ToString()
            );


            var startInfo = new ProcessStartInfo(injectorPath, $"{target.Id} {adapterExecutionArg}");
            startInfo.WorkingDirectory = remoteNetAppDataDir;
            startInfo.UseShellExecute = false;
            startInfo.RedirectStandardOutput = true;
            var injectorProc = Process.Start(startInfo);
            // TODO: Currently I allow 5 sec for the injector to fail (indicated by exiting)
            if (injectorProc != null && injectorProc.WaitForExit(5000))
            {
                // Injector finished early, there's probably an error.
                var stdout = injectorProc.StandardOutput.ReadToEnd();
                throw new Exception("Injector returned error. Raw STDOUT: " + stdout);
            }
            else
            {
                // TODO: There's a bug I can't explain where the injector doesn't finish injecting
                // if it's STDOUT isn't read.
                // This is a hack, it should be solved in another way.
                // CliWrap? Make injector not block on output writes?
                ThreadStart ts = () => { injectorProc.StandardOutput.ReadToEnd(); };
                var readerThread = new Thread(ts)
                {
                    Name = "Injector_STD_Out_Reader",
                    IsBackground = true
                };
                readerThread.Start();
                // TODO: Get results of injector
            }
        }

        public class InjectionToolKit
        {
            public string RemoteNetAppDataDir { get; set; }
            public string InjectorPath { get; set; }
            public string ScubaDiverDllPath { get; set; }
            public string InjectableDummyPath { get; set; }
        }

        private static InjectionToolKit GetInjectionToolkit(Process target, string targetDotNetVer)
        {
            var kit = new InjectionToolKit();


            // Dumping injector + adapter DLL to a %localappdata%\RemoteNET
            kit.RemoteNetAppDataDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                typeof(ManagedRemoteApp).Assembly.GetName().Name);
            DirectoryInfo remoteNetAppDataDirInfo = new DirectoryInfo(kit.RemoteNetAppDataDir);
            if (!remoteNetAppDataDirInfo.Exists)
            {
                remoteNetAppDataDirInfo.Create();
            }

            GetNativeTools(target, kit);
            GetScubaDiver(target, targetDotNetVer, kit);

            return kit;
        }

        private static void GetScubaDiver(Process target, string targetDotNetVer, InjectionToolKit kit)
        {
            bool isNetCore = targetDotNetVer != "net451";
            bool isNet5 = targetDotNetVer == "net5.0-windows";
            bool isNet6orUp = targetDotNetVer == "net6.0-windows" ||
                              targetDotNetVer == "net7.0-windows";
            bool isNative = targetDotNetVer == "native";


            // Unzip scuba diver and dependencies into their own directory
            string targetDiver = "ScubaDiver_NetFramework";
            if (isNetCore)
                targetDiver = "ScubaDiver_NetCore";
            if (isNet5)
                targetDiver = "ScubaDiver_Net5";
            if (isNet6orUp || isNative)
                targetDiver = target.Is64Bit() ? "ScubaDiver_Net6_x64" : "ScubaDiver_Net6_x86";

            var scubaDestDirInfo = new DirectoryInfo(
                Path.Combine(
                    kit.RemoteNetAppDataDir,
                    targetDiver
                ));
            if (!scubaDestDirInfo.Exists)
            {
                scubaDestDirInfo.Create();
            }

            // Temp dir to dump to before moving to app data (where it might have previously deployed files
            // AND they might be in use by some application so they can't be overwritten)
            Random rand = new Random();
            var tempDir = Path.Combine(
                Path.GetTempPath(),
                rand.Next(100000).ToString());
            DirectoryInfo tempDirInfo = new DirectoryInfo(tempDir);
            if (tempDirInfo.Exists)
            {
                tempDirInfo.Delete(recursive: true);
            }

            tempDirInfo.Create();
            using (var diverZipMemoryStream = new MemoryStream(Resources.ScubaDivers))
            {
                ZipArchive diverZip = new ZipArchive(diverZipMemoryStream);
                // This extracts the "Scuba" directory from the zip to *tempDir*
                diverZip.ExtractToDirectory(tempDir);
            }

            // Going over unzipped files and checking which of those we need to copy to our AppData directory
            tempDirInfo = new DirectoryInfo(Path.Combine(tempDir, targetDiver));
            foreach (FileInfo fileInfo in tempDirInfo.GetFiles())
            {
                string destPath = Path.Combine(scubaDestDirInfo.FullName, fileInfo.Name);
                if (File.Exists(destPath))
                {
                    string dumpedFileHash = HashUtils.FileSHA256(fileInfo.FullName);
                    string previousFileHash = HashUtils.FileSHA256(destPath);
                    if (dumpedFileHash == previousFileHash)
                    {
                        // Skipping file because the previous version of it has the same hash
                        continue;
                    }
                }

                // Moving file to our AppData directory
                File.Delete(destPath);
                fileInfo.MoveTo(destPath);
                // Also set the copy's permissions so we can inject it into UWP apps
                AllowUwpInjection(destPath);
            }


            // We are done with our temp directory
            tempDirInfo.Delete(recursive: true);

            // Look for the specific scuba diver according to the target's .NET version
            var matches = scubaDestDirInfo.EnumerateFiles().Where(scubaFile => scubaFile.Name.EndsWith($"{targetDiver}.dll"));
            if (matches.Count() != 1)
            {
                Debugger.Launch();
                throw new Exception($"Expected exactly 1 ScubaDiver dll to match '{targetDiver}' but found: " +
                                    matches.Count() + "\n" +
                                    "Results: \n" +
                                    String.Join("\n", matches.Select(m => m.FullName)) +
                                    "Target Framework Parameter: " +
                                    targetDotNetVer
                );
            }

            kit.ScubaDiverDllPath = matches.Single().FullName;
        }


        private static void GetNativeTools(Process target, InjectionToolKit kit)
        {
            // Decide which injection toolkit to use x32 or x64
            kit.InjectorPath = Path.Combine(kit.RemoteNetAppDataDir, nameof(Resources.Injector) + ".exe");
            string adapterPath = Path.Combine(kit.RemoteNetAppDataDir, nameof(Resources.UnmanagedAdapterDLL) + ".dll");
            string adapterPdbPath = Path.Combine(kit.RemoteNetAppDataDir, nameof(Resources.UnmanagedAdapterDLL) + ".pdb");
            byte[] injectorResource = Resources.Injector;
            byte[] adapterResource = Resources.UnmanagedAdapterDLL;
            byte[] adapterPdbResource = Resources.UnmanagedAdapterDLL_pdb;
            if (target.Is64Bit())
            {
                kit.InjectorPath = Path.Combine(kit.RemoteNetAppDataDir, nameof(Resources.Injector_x64) + ".exe");
                adapterPath = Path.Combine(kit.RemoteNetAppDataDir, nameof(Resources.UnmanagedAdapterDLL_x64) + ".dll");
                adapterPdbPath = Path.Combine(kit.RemoteNetAppDataDir, nameof(Resources.UnmanagedAdapterDLL_x64) + ".pdb");
                injectorResource = Resources.Injector_x64;
                adapterResource = Resources.UnmanagedAdapterDLL_x64;
                adapterPdbResource = Resources.UnmanagedAdapterDLL_x64_pdb;
            }

            // Check if injector or bootstrap resources differ from copies on disk
            string injectorResourceHash = HashUtils.BufferSHA256(injectorResource);
            string injectorFileHash = File.Exists(kit.InjectorPath) ? HashUtils.FileSHA256(kit.InjectorPath) : String.Empty;
            if (injectorResourceHash != injectorFileHash)
            {
                File.WriteAllBytes(kit.InjectorPath, injectorResource);
            }

            string adapterResourceHash = HashUtils.BufferSHA256(adapterResource);
            string adapterFileHash = File.Exists(adapterPath) ? HashUtils.FileSHA256(adapterPath) : String.Empty;
            if (adapterResourceHash != adapterFileHash)
            {
                File.WriteAllBytes(adapterPath, adapterResource);
                File.WriteAllBytes(adapterPdbPath, adapterPdbResource);
                // Also set the copy's permissions so we can inject it into UWP apps
                AllowUwpInjection(adapterPath);
            }

            // Check if InjectableDummyPath resources differ from copies on disk
            kit.InjectableDummyPath = Path.Combine(kit.RemoteNetAppDataDir, nameof(Resources.InjectableDummy) + ".dll");
            string injectableDummyPdbPath = Path.Combine(kit.RemoteNetAppDataDir, nameof(Resources.InjectableDummy) + ".pdb");
            string injectableDummyRuntimeConfigPath = Path.Combine(kit.RemoteNetAppDataDir,
                nameof(Resources.InjectableDummy) + ".runtimeconfig.json");
            // Write to disk
            File.WriteAllBytes(kit.InjectableDummyPath, Resources.InjectableDummy);
            File.WriteAllBytes(injectableDummyPdbPath, Resources.InjectableDummyPdb);
            File.WriteAllBytes(injectableDummyRuntimeConfigPath, Resources.InjectableDummy_runtimeconfig);
            // Change permissions
            AllowUwpInjection(kit.InjectableDummyPath);
            AllowUwpInjection(injectableDummyPdbPath);
            AllowUwpInjection(injectableDummyRuntimeConfigPath);
        }

        private static void AllowUwpInjection(string destPath)
        {
            // See: https://stackoverflow.com/a/52852183
            FilePermissions.AddFileSecurity(destPath, "ALL APPLICATION PACKAGES",
                System.Security.AccessControl.FileSystemRights.ReadAndExecute,
                System.Security.AccessControl.AccessControlType.Allow);
        }
    }
}