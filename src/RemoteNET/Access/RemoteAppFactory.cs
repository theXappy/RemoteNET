using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Management;
using System.Threading;
using InjectableDotNetHost.Injector;
using RemoteNET.Internal;
using RemoteNET.Internal.Extensions;
using RemoteNET.Utils;
using ScubaDiver.API;

namespace RemoteNET.Access
{
    public enum ConnectionStrategy
    {
        Unknown,
        DllInjection,
        DllHijack,
    }

    public static partial class RemoteAppFactory
    {
        public static RemoteApp Connect(string targetQuery, RuntimeType runtime, ConnectionConfig config = null)
        {
            config ??= ConnectionConfig.Default;

            try
            {
                Process target = ProcessHelper.GetSingleRoot(targetQuery);
                return Connect(target, runtime, config);
            }
            catch (TooManyProcessesException tooManyProcsEx)
            {
                throw new TooManyProcessesException($"{tooManyProcsEx.Message}\n" +
                                                    $"You can also get the right {nameof(Process)} object yourself and use the " +
                                                    $"overload with the {nameof(Process)} parameter to avoid this issue.", tooManyProcsEx.Matches);
            }
        }

        /// <summary>
        /// Creates a new provider.
        /// </summary>
        /// <param name="target">Process to create the provider for</param>
        /// <param name="runtime">Runtime type to connect to</param>
        /// <param name="config">Connection configuration including strategy and DLL proxy settings</param>
        /// <returns>A provider for the given process</returns>
        public static RemoteApp Connect(Process target, RuntimeType runtime, ConnectionConfig config = null)
        {
            config ??= ConnectionConfig.Default;

            if (config.Strategy == ConnectionStrategy.Unknown)
                config.Strategy = ConnectionStrategy.DllInjection;

            //
            // First Try: Use discovery to check for existing diver
            //

            // To make the Diver's port's predictable even when re-attaching we'll derive it from the PID:
            ushort diverPort = (ushort)target.Id;

            DiverDiscovery.QueryStatus(target, out DiverState managedState, out DiverState unmanagedState);
            switch (managedState)
            {
                case DiverState.Alive:
                    // We can reconnect to the existing diver!
                    break;
                case DiverState.Corpse:
                    if (unmanagedState == DiverState.Alive)
                        break;
                    throw new Exception("Failed to connect to remote app. It seems like the diver had already been injected but it is not responding to HTTP requests.\n" +
                                        "It's suggested to restart the target app and retry.");

                case DiverState.HollowSnapshot:
                    throw new Exception("Failed to connect to remote app. Hollow Snapshot.");
                case DiverState.NoDiver:
                    target = RunStrategy(target, diverPort, config);
                    break;
            }

            // Port might've changed if the strategy re-started the app
            diverPort = (ushort)target.Id;

            // Now register our program as a "client" of the diver
            string diverAddr = "127.0.0.1";

            // ALWAYS connecting a managed app.
            // Sometimes this will be the return value.
            // Sometimes it'll just assist our Unmanaged App (which will be the return value)
            RemoteAppsHub hub = new RemoteAppsHub();
            DiverCommunicator managedCom = new DiverCommunicator(diverAddr, diverPort);
            ManagedRemoteApp managedApp = null;
            if (managedCom.RegisterClient())
            {
                managedApp = new ManagedRemoteApp(target, managedCom, hub);
                hub[RuntimeType.Managed] = managedApp;
            }

            switch (runtime)
            {
                case RuntimeType.Managed:
                    if (managedApp != null)
                        return managedApp;
                    break;
                case RuntimeType.Unmanaged:
                    DiverCommunicator unmanagedCom = new DiverCommunicator(diverAddr, diverPort + 2);
                    if (unmanagedCom.RegisterClient())
                        return new UnmanagedRemoteApp(target, unmanagedCom, hub);
                    break;
                case RuntimeType.Unknown:
                default:
                    throw new ArgumentOutOfRangeException(nameof(runtime), runtime, null);
            }

            // Enhanced error information
            bool isTargetAlive = !target.HasExited;
            string aliveStatus = isTargetAlive ? "ALIVE" : "EXITED";
            
            string targetInfo = $"Target Process: PID={target.Id}, Name='{target.ProcessName}', Status={aliveStatus}";
            if (isTargetAlive)
            {
                try 
                {
                    targetInfo += $", Path='{target.MainModule?.FileName ?? "Unknown"}'";
                }
                catch (Exception ex)
                {
                    targetInfo += $", Path=<Error: {ex.Message}>";
                }
            }
            
            string connectionInfo = $"Connection: DiverAddress={diverAddr}, DiverPort={diverPort}, RequestedRuntime={runtime}";
            string strategyInfo = $"Strategy: {config.Strategy}" + (config.TargetDllToProxy != null ? $", TargetDll='{config.TargetDllToProxy}'" : "");
            string diverStates = $"DiverDiscovery: ManagedState={managedState}, UnmanagedState={unmanagedState}";
            string clientRegInfo = $"ClientRegistration: ManagedClient={managedApp != null}, RequestedRuntime={runtime}";
            
            throw new Exception($"Registering our current app as a client in the Diver failed.\n" +
                               $"{targetInfo}\n" +
                               $"{connectionInfo}\n" +
                               $"{strategyInfo}\n" +
                               $"{diverStates}\n" +
                               $"{clientRegInfo}");
        }

        // Legacy overloads for backward compatibility
        [Obsolete("Use Connect(target, runtime, ConnectionConfig) instead")]
        public static RemoteApp Connect(string targetQuery, RuntimeType runtime, ConnectionStrategy strat, string targetDllToProxy = null)
        {
            var config = new ConnectionConfig { Strategy = strat, TargetDllToProxy = targetDllToProxy };
            return Connect(targetQuery, runtime, config);
        }

        [Obsolete("Use Connect(target, runtime, ConnectionConfig) instead")]
        public static RemoteApp Connect(Process target, RuntimeType runtime, ConnectionStrategy strat, string targetDllToProxy = null)
        {
            var config = new ConnectionConfig { Strategy = strat, TargetDllToProxy = targetDllToProxy };
            return Connect(target, runtime, config);
        }

        private static Process RunStrategy(Process target, ushort diverPort, ConnectionConfig config)
        {
            // Determine if we are dealing with .NET Framework vs .NET Core
            string targetDotNetVer = target.GetSupportedTargetFramework();

            // Get different injection kit (for .NET Framework vs .NET Core & x86 vs x64)
            InjectionToolKit kit = InjectionToolKit.GetKit(target, targetDotNetVer);

            switch (config.Strategy)
            {
                case ConnectionStrategy.DllInjection:
                    InjectDiver(target, diverPort, targetDotNetVer, kit);
                    break;
                case ConnectionStrategy.DllHijack:
                    target = HijackDllAndRelaunch(target, kit, config.TargetDllToProxy);
                    break;
                case ConnectionStrategy.Unknown:
                default:
                    throw new ArgumentOutOfRangeException(nameof(config.Strategy), config.Strategy, null);
            }

            return target;
        }

        private static Process HijackDllAndRelaunch(Process target, InjectionToolKit kit, string targetDllToProxy)
        {
            // Store command line arguments of the target process
            string exePath = target.MainModule.FileName;
            string targetCmdLine = ""; // GetCommandLineArgs(target.Id);

            // Check if a DLL was already hijacked
            string hijackInfoFilePath = exePath + ".remotenet_hijack";
            bool alreadyHijacked = File.Exists(hijackInfoFilePath);
            if (!alreadyHijacked)
            {
                string dllToProxy = targetDllToProxy;

                if (string.IsNullOrWhiteSpace(dllToProxy))
                {
                    // Find a target module to proxy
                    var targetModules = SharpDllProxy.VictimModuleFinder.Search(target);
                    SharpDllProxy.VictimModuleFinder.VictimModuleInfo victim = targetModules.First();
                    dllToProxy = victim.OriginalFilePath;
                }

                SharpDllProxy.ProxyCreator proxyCreator = new SharpDllProxy.ProxyCreator(s => Debug.WriteLine(s));
                SharpDllProxy.ProxyCreatorResults proxyResults = proxyCreator.CreateProxy(dllToProxy, kit.UnmanagedAdapterPath, "PromptEntryPoint");

                // Terminate target process
                target.Kill();

                // Rename original victim module (Keep adding "_bak" extensions until it's unique, up to 3 times)
                string victimBackupPath = dllToProxy;
                for (int i = 0; File.Exists(victimBackupPath) && i < 3; i++)
                    victimBackupPath += "_bak";
                File.Move(dllToProxy, victimBackupPath);

                // Copy the output DLL, replacing the victim module
                File.Copy(proxyResults.OutputDll, dllToProxy);

                // Copy proxied DLL alongside of it
                string proxiedFileName = Path.GetFileName(proxyResults.ProxiedDll);
                string proxyDllPath = Path.Combine(Path.GetDirectoryName(dllToProxy), proxiedFileName);
                File.Copy(proxyResults.ProxiedDll, proxyDllPath);
            }
            else
            {
                // Terminate target process
                target.Kill();
            }

            // Relaunch the target process with the same command line arguments
            // Set working directory to the directory containing the target executable
            string exeDirectory = Path.GetDirectoryName(exePath);
            ProcessStartInfo psi = new ProcessStartInfo(exePath, targetCmdLine)
            {
                WorkingDirectory = exeDirectory,
                UseShellExecute = false
            };
            Process newProc = Process.Start(psi);

            // Write hijack indicator: <output_dll_path>;<proxied_dll_path>
            File.WriteAllText(hijackInfoFilePath, $"{kit.UnmanagedAdapterPath};{kit.UnmanagedAdapterPath}");
            return newProc;
        }

        private static void InjectDiver(Process target, ushort diverPort, string targetDotNetVer, InjectionToolKit kit)
        {
            string scubaDiverArgs = diverPort.ToString();
            if (target.IsUwpApp())
            {
                RunLifeboat(kit.LifeboatExePath, diverPort);

                // Changing the ScubaDiver's argument so it knows to connect in "reverse"
                // to the Lifeboat proxy.
                scubaDiverArgs += "~reverse";
            }

            // If we have a native target, We try to host a .NET Core runtime inside.
            if (targetDotNetVer == "native")
                HostDotNetRuntime(target, kit);

            string adapterExecutionArg = string.Join("*",
                kit.ScubaDiverDllPath, // assembly
                "ScubaDiver.DllEntry", // class
                "EntryPoint", // method
                scubaDiverArgs,
                targetDotNetVer
            );

            InjectDll(target, kit, adapterExecutionArg);
        }

        private static void HostDotNetRuntime(Process target, InjectionToolKit kit)
        {
            var hostInjector = new DotNetHostInjector(new DotNetHostInjectorOptions());
            var results = hostInjector.Inject(target, kit.InjectableDummyPath, "InjectableDummy.DllMain, InjectableDummy");
            Debug.WriteLine("hostInjector.Inject RESULTS: " + results);
            if (results != 0)
                throw new Exception(
                    $"DotNetHostInjector failed to host the .NET runtime in the target (PID={target.Id}).\nRaw error:\n" +
                    results);
        }

        private static void RunLifeboat(string lifeboatExePath, ushort diverPort)
        {
            // AFAIK, UWP apps run in network isolation. This means they'll have a hard time running a TCP/HTTP listener.
            // So for those cases we're running Lifeboat, a reverse proxy, and interacting with the diver through it.
            ProcessStartInfo psi = new ProcessStartInfo(lifeboatExePath, diverPort.ToString());
            psi.UseShellExecute = true;
            psi.WindowStyle = ProcessWindowStyle.Hidden;
            psi.CreateNoWindow = true;
            Process.Start(psi);
            psi = new ProcessStartInfo(lifeboatExePath, /*pid*/ diverPort + " " + /*offset*/ "2");
            psi.UseShellExecute = true;
            psi.WindowStyle = ProcessWindowStyle.Hidden;
            psi.CreateNoWindow = true;
            Process.Start(psi);
        }

        private static void InjectDll(Process target, InjectionToolKit kit, string dllArguments)
        {
            string injectorArguments = $"{target.Id} {dllArguments}";
            var injectorStartInfo = new ProcessStartInfo(kit.InjectorPath, injectorArguments)
            {
                WorkingDirectory = kit.RemoteNetAppDataDir,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                CreateNoWindow = true,
                WindowStyle = ProcessWindowStyle.Hidden
            };
            var injectorProc = Process.Start(injectorStartInfo);
            // TODO: Currently I allow 5 sec for the injector to fail (indicated by exiting)
            if (injectorProc != null && injectorProc.WaitForExit(5000))
            {
                // Injector finished early, there's probably an error.
                if (injectorProc.ExitCode != 0)
                {
                    var stdout = injectorProc.StandardOutput.ReadToEnd();
                    throw new Exception("Injector returned error. Raw STDOUT: " + stdout);
                }
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
            Debug.WriteLine($"[RemoteAppFactory] Finished waiting on Injector. Finished: {injectorProc.HasExited}, Code: {injectorProc.ExitCode}");
        }


        public static string GetCommandLineArgs(int processId)
        {
            if (!OperatingSystem.IsWindows())
                throw new PlatformNotSupportedException("This method is only supported on Windows.");

            string commandLine = null;

            using (ManagementObjectSearcher searcher = new ManagementObjectSearcher(
                $"SELECT CommandLine FROM Win32_Process WHERE ProcessId = {processId}"))
            {
                foreach (ManagementObject obj in searcher.Get())
                {
                    commandLine = obj["CommandLine"]?.ToString();
                    break;
                }
            }

            if (string.IsNullOrWhiteSpace(commandLine))
                return string.Empty;

            string trimmed = commandLine.Trim();

            if (trimmed.StartsWith('"'))
                return trimmed.Substring(trimmed.IndexOf('"', 1)).Trim();

            return trimmed.Substring(trimmed.IndexOf(' ')).Trim();
        }
    }
}