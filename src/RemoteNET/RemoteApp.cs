using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Threading;
using RemoteNET.Internal;
using RemoteNET.Internal.Extensions;
using RemoteNET.Internal.Reflection;
using RemoteNET.Internal.Utils;
using RemoteNET.Properties;
using RemoteNET.Utils;
using ScubaDiver.API;
using ScubaDiver.API.Dumps;

namespace RemoteNET
{
    public class RemoteApp : IDisposable
    {
        internal class RemoteObjectsCollection
        {
            // The WeakReferences are to RemoteObject
            private readonly Dictionary<ulong, WeakReference<RemoteObject>> _pinnedAddressesToRemoteObjects;
            private readonly RemoteApp _app;

            public RemoteObjectsCollection(RemoteApp app)
            {
                _app = app;
                _pinnedAddressesToRemoteObjects = new Dictionary<ulong, WeakReference<RemoteObject>>();
            }

            private RemoteObject GetRemoteObjectUncached(ulong remoteAddress, int? hashCode = null)
            {
                ObjectDump od;
                TypeDump td;
                try
                {
                    od = _app._communicator.DumpObject(remoteAddress, true, hashCode);
                    td = _app._communicator.DumpType(od.Type);
                }
                catch (Exception e)
                {
                    throw new Exception("Could not dump remote object/type.", e);
                }


                var remoteObject = new RemoteObject(new RemoteObjectRef(od, td, _app._communicator), _app);
                return remoteObject;
            }

            public RemoteObject GetRemoteObject(ulong address, int? hashcode = null)
            {
                RemoteObject ro;
                if (_pinnedAddressesToRemoteObjects.TryGetValue(address, out WeakReference<RemoteObject> wr))
                {
                    bool gotTarget = wr.TryGetTarget(out ro);
                    if (gotTarget)
                    {
                        // Not GC'd!
                        return ro;
                    }
                    else
                    {
                        // Object was GC'd...
                        _pinnedAddressesToRemoteObjects.Remove(address);
                        // Now let's make sure the GC'd object finalizer was also called (otherwise some "object moved" errors might happen).
                        GC.WaitForPendingFinalizers();
                        // Now we need to-read the since stuff might have moved
                    }
                }

                // Get remote
                ro = this.GetRemoteObjectUncached(address, hashcode);
                wr = new WeakReference<RemoteObject>(ro);
                _pinnedAddressesToRemoteObjects[ro.RemoteToken] = wr;

                return ro;
            }
        }

        private Process _procWithDiver;
        private DiverCommunicator _communicator;

        private readonly RemoteObjectsCollection _remoteObjects;

        public Process Process => _procWithDiver;
        public RemoteActivator Activator { get; private set; }
        public RemoteHarmony Harmony { get; private set; }

        public DiverCommunicator Communicator => _communicator;

        private RemoteApp(Process procWithDiver, DiverCommunicator communicator)
        {
            _procWithDiver = procWithDiver;
            _communicator = communicator;
            Activator = new RemoteActivator(communicator, this);
            Harmony = new RemoteHarmony(this);
            _remoteObjects = new RemoteObjectsCollection(this);
        }

        //
        // Init
        // 

        public static RemoteApp Connect(string target)
        {
            try
            {
                return Connect(ProcessHelper.GetSingleRoot(target));
            }
            catch (TooManyProcessesException tooManyProcsEx)
            {
                throw new TooManyProcessesException($"{tooManyProcsEx.Message}\n" +
                    $"You can also get the right System.Diagnostics.Process object yourself and use the {nameof(Connect)}({nameof(Process)} target) overload of this function.", tooManyProcsEx.Matches);
            }
        }

        /// <summary>
        /// Creates a new provider.
        /// </summary>
        /// <param name="target">Process to create the provider for</param>
        /// <returns>A provider for the given process</returns>
        public static RemoteApp Connect(Process target)
        {
            // TODO: If target is our own process run a local Diver without DLL injections

            //
            // First Try: Use discovery to check for existing diver
            //

            // To make the Diver's port predictable even when re-attaching we'll derive it from the PID:
            ushort diverPort = (ushort)target.Id;
            // TODO: Make it configurable

            DiverState status = DiverDiscovery.QueryStatus(target);

            if (status == DiverState.Alive)
            {
                // Everything's fine, we can continue with the existing diver
            }
            else if (status == DiverState.Corpse)
            {
                throw new Exception("Failed to connect to remote app. It seems like the diver had already been injected but it is not responding to HTTP requests.\n" +
                    "It's suggested to restart the target app and retry.");
            }
            else if (status == DiverState.NoDiver)
            {
                //
                // Second Try: Inject DLL, assuming not injected yet
                //

                // Determine if we are dealing with .NET Framework or .NET Core
                string targetDotNetVer = target.GetSupportedTargetFramework();
                if(targetDotNetVer == "native")
                {
                    throw new ArgumentException($"Process {target.ProcessName} does not seem to be a .NET Framework or .NET Core app. Can't inject to native apps.");
                }
                bool isNetCore = targetDotNetVer != "net451";


                // Not injected yet, Injecting adapter now (which should load the Diver)
                // Get different injection kit (for .NET framework or .NET core & x86 or x64)
                GetInjectionToolkit(target, isNetCore, out string remoteNetAppDataDir, out string injectorPath, out string scubaDiverDllPath);
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
                // TODO: Currently I allow 500ms for the injector to fail (indicated by exiting)
                if (injectorProc != null && injectorProc.WaitForExit(500))
                {
                    // Injector finished early, there's probably an error.
                    var stdout = injectorProc.StandardOutput.ReadToEnd();
                    throw new Exception("Injector returned error. Raw STDOUT: " + stdout);
                }
                else
                {
                    // TODO: There's a bug I can't explain where the injector doesnt finish injecting
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

            // Now register our program as a "client" of the diver
            string diverAddr = "127.0.0.1";
            DiverCommunicator com = new DiverCommunicator(diverAddr, diverPort);
            bool registered = com.RegisterClient();
            if (registered)
            {
                return new RemoteApp(target, com);
            }
            else
            {
                throw new Exception("Registering our current app as a client in the Diver failed.");
            }
        }

        private static void GetInjectionToolkit(Process target, bool isNetCore, out string remoteNetAppDataDir, out string injectorPath, out string scubaDiverDllPath)
        {
            // Dumping injector + adapter DLL to a %localappdata%\RemoteNET
            remoteNetAppDataDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                typeof(RemoteApp).Assembly.GetName().Name);
            DirectoryInfo remoteNetAppDataDirInfo = new DirectoryInfo(remoteNetAppDataDir);
            if (!remoteNetAppDataDirInfo.Exists)
            {
                remoteNetAppDataDirInfo.Create();
            }

            // Decide which injection toolkit to use x32 or x64
            injectorPath = Path.Combine(remoteNetAppDataDir, nameof(Resources.Injector) + ".exe");
            string adapterPath = Path.Combine(remoteNetAppDataDir, nameof(Resources.UnmanagedAdapterDLL) + ".dll");
            byte[] injectorResource = Resources.Injector;
            byte[] adapterResource = Resources.UnmanagedAdapterDLL;
            if (target.Is64Bit())
            {
                injectorPath = Path.Combine(remoteNetAppDataDir, nameof(Resources.Injector_x64) + ".exe");
                adapterPath = Path.Combine(remoteNetAppDataDir, nameof(Resources.UnmanagedAdapterDLL_x64) + ".dll");
                injectorResource = Resources.Injector_x64;
                adapterResource = Resources.UnmanagedAdapterDLL_x64;
            }

            // Check if injector or bootstrap resources differ from copies on disk
            string injectorResourceHash = HashUtils.BufferSHA256(injectorResource);
            string injectorFileHash = File.Exists(injectorPath) ? HashUtils.FileSHA256(injectorPath) : String.Empty;
            if (injectorResourceHash != injectorFileHash)
            {
                File.WriteAllBytes(injectorPath, injectorResource);
            }
            string adapterResourceHash = HashUtils.BufferSHA256(adapterResource);
            string adapterFileHash = File.Exists(adapterPath) ? HashUtils.FileSHA256(adapterPath) : String.Empty;
            if (adapterResourceHash != adapterFileHash)
            {
                File.WriteAllBytes(adapterPath, adapterResource);
                // Also set the copy's permissions so we can inject it into UWP apps
                FilePermissions.AddFileSecurity(adapterPath, "ALL APPLICATION PACKAGES",
                    System.Security.AccessControl.FileSystemRights.ReadAndExecute,
                    System.Security.AccessControl.AccessControlType.Allow);
            }

            // Unzip scuba diver and dependencies into their own directory
            var scubaDestDirInfo = new DirectoryInfo(
                                            Path.Combine(
                                                remoteNetAppDataDir,
                                                isNetCore ? "Scuba_NetCore" : "Scuba")
                                            );
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
            using (var diverZipMemoryStream = new MemoryStream(isNetCore ? Resources.ScubaDiver_NetCore : Resources.ScubaDiver))
            {
                ZipArchive diverZip = new ZipArchive(diverZipMemoryStream);
                // This extracts the "Scuba" directory from the zip to *tempDir*
                diverZip.ExtractToDirectory(tempDir);
            }

            // Going over unzipped files and checking which of those we need to copy to our AppData directory
            tempDirInfo = new DirectoryInfo(Path.Combine(tempDir, isNetCore ? "Scuba_NetCore" : "Scuba"));
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
                FilePermissions.AddFileSecurity(destPath, "ALL APPLICATION PACKAGES",
                            System.Security.AccessControl.FileSystemRights.ReadAndExecute,
                            System.Security.AccessControl.AccessControlType.Allow);
            }


            // We are done with our temp directory
            tempDirInfo.Delete(recursive: true);
            if (isNetCore)
            {
                Logger.Debug("[DEBUG] .NET Core target!");
                scubaDiverDllPath = scubaDestDirInfo.EnumerateFiles()
                   .Single(scubaFile => scubaFile.Name.EndsWith("ScubaDiver_NetCore.dll")).FullName;
            }
            else
            {
                scubaDiverDllPath = scubaDestDirInfo.EnumerateFiles()
                .Single(scubaFile => scubaFile.Name.EndsWith("ScubaDiver.dll")).FullName;
            }
        }

        //
        // Remote Heap querying
        //

        public IEnumerable<CandidateObject> QueryInstances(Type typeFilter) => QueryInstances(typeFilter.FullName);
        /// <summary>
        /// Gets all object candidates for a specific filter
        /// </summary>
        /// <param name="typeFullNameFilter">Objects with Full Type Names of this EXACT string will be returned. You can use '*' as a "0 or more characters" wildcard</param>
        public IEnumerable<CandidateObject> QueryInstances(string typeFullNameFilter)
        {
            return _communicator.DumpHeap(typeFullNameFilter).Objects.Select(heapObj => new CandidateObject(heapObj.Address, heapObj.Type, heapObj.HashCode));
        }

        //
        // Resolving Types
        //

        /// <summary>
        /// Gets a handle to a remote type (even ones from assemblies we aren't referencing/loading to the local process)
        /// </summary>
        /// <param name="typeFullName">Full name of the type to get. For example 'System.Xml.XmlDocument'</param>
        /// <param name="assembly">Optional short name of the assembly containing the type. For example 'System.Xml.ReaderWriter.dll'</param>
        /// <returns></returns>
        public Type GetRemoteType(string typeFullName, string assembly = null)
        {
            // Easy case: Trying to resolve from cache or from local assemblies
            var resolver = TypesResolver.Instance;
            Type res = resolver.Resolve(assembly, typeFullName);
            if(res != null)
            {
                // Either found in cache or found locally.

                // If it's a local type we need to wrap it in a "fake" RemoteType (So method invocations will actually 
                // happend in the remote app, for example)
                // (But not for primitives...)
                if (!(res is RemoteType) && !res.IsPrimitive)
                {
                    res = new RemoteType(this, res);
                    // TODO: Registring here in the cache is a hack but we couldn't register within "TypesResolver.Resolve"
                    // because we don't have the RemoteApp to associate the fake remote type with.
                    // Maybe this should move somewhere else...
                    resolver.RegisterType(res);
                }
                return res;
            }

            // Harder case: Dump the remote type. This takes much more time (includes dumping of depedent
            // types) and should be avoided as much as possible.
            RemoteTypesFactory rtf = new RemoteTypesFactory(resolver, _communicator, avoidGenericsRecursion: true);
            var dumpedType = _communicator.DumpType(typeFullName, assembly);
            return rtf.Create(this, dumpedType);
        }
        /// <summary>
        /// Returns a handle to a remote type based on a given local type.
        /// </summary>
        public Type GetRemoteType(Type localType) => GetRemoteType(localType.FullName, localType.Assembly.GetName().Name);
        internal Type GetRemoteType(TypeDump typeDump) => GetRemoteType(typeDump.Type, typeDump.Assembly);

        public RemoteEnum GetRemoteEnum(string typeFullName, string assembly = null)
        {
            RemoteType remoteType = GetRemoteType(typeFullName, assembly) as RemoteType;
            if (remoteType == null)
            {
                throw new Exception("Failed to dump remote enum (and get a RemoteType object)");
            }
            return new RemoteEnum(remoteType);
        }

        //
        // Getting Remote Objects
        //

        public RemoteObject GetRemoteObject(CandidateObject candidate) => GetRemoteObject(candidate.Address, candidate.HashCode);
        public RemoteObject GetRemoteObject(ulong remoteAddress, int? hashCode = null)
        {
            return _remoteObjects.GetRemoteObject(remoteAddress, hashCode);
        }

        //
        // IDisposable
        //
        public void Dispose()
        {
            Communicator?.KillDiver();
            _communicator = null;
            _procWithDiver = null;
        }

    }
}