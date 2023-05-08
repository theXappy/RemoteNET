using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Reflection;
using System.Threading;
using InjectableDotNetHost.Injector;
using RemoteNET.Internal;
using RemoteNET.Internal.Extensions;
using RemoteNET.Internal.Reflection;
using RemoteNET.Internal.Utils;
using RemoteNET.Properties;
using RemoteNET.Utils;
using ScubaDiver.API;
using ScubaDiver.API.Interactions.Dumps;
using ScubaDiver.API.Utils;

namespace RemoteNET
{
    public class RemoteApp : IDisposable
    {
        internal class RemoteObjectsCollection
        {
            // The WeakReferences are to RemoteObject
            private readonly Dictionary<ulong, WeakReference<RemoteObject>> _pinnedAddressesToRemoteObjects;
            private readonly object _lock = new object();

            private readonly RemoteApp _app;

            public RemoteObjectsCollection(RemoteApp app)
            {
                _app = app;
                _pinnedAddressesToRemoteObjects = new Dictionary<ulong, WeakReference<RemoteObject>>();
            }

            private RemoteObject GetRemoteObjectUncached(ulong remoteAddress, string typeName, int? hashCode = null)
            {
                ObjectDump od;
                ManagedTypeDump td;
                try
                {
                    od = _app._managedCommunicator.DumpObject(remoteAddress, typeName, true, hashCode);
                    td = _app._managedCommunicator.DumpType(od.Type);
                }
                catch (Exception e)
                {
                    throw new Exception("Could not dump remote object/type.", e);
                }


                var remoteObject = new RemoteObject(new RemoteObjectRef(od, td, _app._managedCommunicator), _app);
                return remoteObject;
            }

            public RemoteObject GetRemoteObject(ulong address, string typeName, int? hashcode = null)
            {
                RemoteObject ro;
                WeakReference<RemoteObject> weakRef;
                // Easiert way - Non-collected and previouslt obtained object ("Cached")
                if (_pinnedAddressesToRemoteObjects.TryGetValue(address, out weakRef) &&
                    weakRef.TryGetTarget(out ro))
                {
                    // Not GC'd!
                    return ro;
                }

                // Harder case - At time of checking, item wasn't cached.
                // We need exclusive access to the cahce now to make sure we are the only one adding it.
                lock (_lock)
                {
                    // Last chance - when we waited on the lock some other thread might've added it to the cache.
                    if (_pinnedAddressesToRemoteObjects.TryGetValue(address, out weakRef))
                    {
                        bool gotTarget = weakRef.TryGetTarget(out ro);
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
                            // Now we need to-read the remote object since stuff might have moved
                        }
                    }

                    // Get remote
                    ro = this.GetRemoteObjectUncached(address, typeName, hashcode);
                    // Add to cache
                    weakRef = new WeakReference<RemoteObject>(ro);
                    _pinnedAddressesToRemoteObjects[ro.RemoteToken] = weakRef;
                }

                return ro;
            }
        }

        private Process _procWithDiver;
        private DiverCommunicator _managedCommunicator;
        private DomainsDump _managedDomains;
        private readonly RemoteObjectsCollection _remoteObjects;

        private DiverCommunicator _unmanagedCommunicator;
        private List<string> _unmanagedModulesList;

        public Process Process => _procWithDiver;
        public RemoteActivator Activator { get; private set; }
        public RemoteHarmony Harmony { get; private set; }

        public DiverCommunicator ManagedCommunicator => _managedCommunicator;
        public DiverCommunicator UnanagedCommunicator => _managedCommunicator;

        private RemoteApp(Process procWithDiver, DiverCommunicator managedCommunicator, DiverCommunicator unmanagedCommunicator)
        {
            _procWithDiver = procWithDiver;
            _managedCommunicator = managedCommunicator;
            _unmanagedCommunicator = unmanagedCommunicator;
            Activator = new RemoteActivator(managedCommunicator, this);
            Harmony = new RemoteHarmony(this);
            _remoteObjects = new RemoteObjectsCollection(this);
            _unmanagedCommunicator = unmanagedCommunicator;
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

            // Now register our program as a "client" of the diver
            string diverAddr = "127.0.0.1";
            DiverCommunicator managedCom = new DiverCommunicator(diverAddr, diverPort);
            DiverCommunicator unmanagedCom = new DiverCommunicator(diverAddr, diverPort + 2);


            bool registeredManaged = managedCom.RegisterClient();
            if (registeredManaged)
            {
                // Unmanaged Communicator talks to the MsvcDiver, which is optional in the target.
                // Check if we got one or not.
                bool registeredUnmanaged = unmanagedCom.RegisterClient();
                if (!registeredUnmanaged)
                {
                    // We'll continue without unmanaged capabilities
                    unmanagedCom = null;
                }

                return new RemoteApp(target, managedCom, unmanagedCom);
            }
            else
            {
                throw new Exception("Registering our current app as a client in the Diver failed.");
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
            InjectionToolKit output = new InjectionToolKit();

            bool isNetCore = targetDotNetVer != "net451";
            bool isNet5 = targetDotNetVer == "net5.0-windows";
            bool isNet6orUp = targetDotNetVer == "net6.0-windows" ||
                              targetDotNetVer == "net7.0-windows";
            bool isNative = targetDotNetVer == "native";

            // Dumping injector + adapter DLL to a %localappdata%\RemoteNET
            output.RemoteNetAppDataDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                typeof(RemoteApp).Assembly.GetName().Name);
            DirectoryInfo remoteNetAppDataDirInfo = new DirectoryInfo(output.RemoteNetAppDataDir);
            if (!remoteNetAppDataDirInfo.Exists)
            {
                remoteNetAppDataDirInfo.Create();
            }

            // Decide which injection toolkit to use x32 or x64
            output.InjectorPath = Path.Combine(output.RemoteNetAppDataDir, nameof(Resources.Injector) + ".exe");
            string adapterPath = Path.Combine(output.RemoteNetAppDataDir, nameof(Resources.UnmanagedAdapterDLL) + ".dll");
            string adapterPdbPath = Path.Combine(output.RemoteNetAppDataDir, nameof(Resources.UnmanagedAdapterDLL) + ".pdb");
            byte[] injectorResource = Resources.Injector;
            byte[] adapterResource = Resources.UnmanagedAdapterDLL;
            byte[] adapterPdbResource = Resources.UnmanagedAdapterDLL_pdb;
            if (target.Is64Bit())
            {
                output.InjectorPath = Path.Combine(output.RemoteNetAppDataDir, nameof(Resources.Injector_x64) + ".exe");
                adapterPath = Path.Combine(output.RemoteNetAppDataDir, nameof(Resources.UnmanagedAdapterDLL_x64) + ".dll");
                adapterPdbPath = Path.Combine(output.RemoteNetAppDataDir, nameof(Resources.UnmanagedAdapterDLL_x64) + ".pdb");
                injectorResource = Resources.Injector_x64;
                adapterResource = Resources.UnmanagedAdapterDLL_x64;
                adapterPdbResource = Resources.UnmanagedAdapterDLL_x64_pdb;
            }

            // Check if injector or bootstrap resources differ from copies on disk
            string injectorResourceHash = HashUtils.BufferSHA256(injectorResource);
            string injectorFileHash = File.Exists(output.InjectorPath) ? HashUtils.FileSHA256(output.InjectorPath) : String.Empty;
            if (injectorResourceHash != injectorFileHash)
            {
                File.WriteAllBytes(output.InjectorPath, injectorResource);
            }
            string adapterResourceHash = HashUtils.BufferSHA256(adapterResource);
            string adapterFileHash = File.Exists(adapterPath) ? HashUtils.FileSHA256(adapterPath) : String.Empty;
            if (adapterResourceHash != adapterFileHash)
            {
                File.WriteAllBytes(adapterPath, adapterResource);
                File.WriteAllBytes(adapterPdbPath, adapterPdbResource);
                // Also set the copy's permissions so we can inject it into UWP apps
                FilePermissions.AddFileSecurity(adapterPath, "ALL APPLICATION PACKAGES",
                    System.Security.AccessControl.FileSystemRights.ReadAndExecute,
                    System.Security.AccessControl.AccessControlType.Allow);
            }

            // Check if InjectableDummyPath resources differ from copies on disk
            output.InjectableDummyPath = Path.Combine(output.RemoteNetAppDataDir, nameof(Resources.InjectableDummy) + ".dll");
            string injectableDummyPdbPath = Path.Combine(output.RemoteNetAppDataDir, nameof(Resources.InjectableDummy) + ".pdb");
            string injectableDummyRuntimeConfigPath = Path.Combine(output.RemoteNetAppDataDir, nameof(Resources.InjectableDummy) + ".runtimeconfig.json");
            File.WriteAllBytes(output.InjectableDummyPath, Resources.InjectableDummy);
            File.WriteAllBytes(injectableDummyPdbPath, Resources.InjectableDummyPdb);
            File.WriteAllBytes(injectableDummyRuntimeConfigPath, Resources.InjectableDummy_runtimeconfig);

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
                                                output.RemoteNetAppDataDir,
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
                FilePermissions.AddFileSecurity(destPath, "ALL APPLICATION PACKAGES",
                            System.Security.AccessControl.FileSystemRights.ReadAndExecute,
                            System.Security.AccessControl.AccessControlType.Allow);
            }


            // We are done with our temp directory
            tempDirInfo.Delete(recursive: true);
            var matches = scubaDestDirInfo.EnumerateFiles().Where(scubaFile => scubaFile.Name.EndsWith($"{targetDiver}.dll"));
            if (matches.Count() != 1)
            {
                Debugger.Launch();
                throw new Exception($"Expected exactly 1 ScubaDiver dll to match '{targetDiver}' but found: " + matches.Count() + "\n" +
                    "Results: \n" +
                    String.Join("\n", matches.Select(m => m.FullName)) +
                    "Target Framework Parameter: " +
                    targetDotNetVer
                    );
            }

            output.ScubaDiverDllPath = matches.Single().FullName;

            return output;
        }

        //
        // Remote Heap querying
        //

        public IEnumerable<CandidateType> QueryTypes(string typeFullNameFilter)
        {
            Predicate<string> matchesFilter = Filter.CreatePredicate(typeFullNameFilter);

            _managedDomains ??= _managedCommunicator.DumpDomains();
            foreach (DomainsDump.AvailableDomain domain in _managedDomains.AvailableDomains)
            {
                foreach (string assembly in domain.AvailableModules)
                {
                    List<TypesDump.TypeIdentifiers> typeIdentifiers;
                    try
                    {
                        typeIdentifiers = _managedCommunicator.DumpTypes(assembly).Types;
                    }
                    catch
                    {
                        // TODO:
                        Debug.WriteLine($"[{nameof(RemoteApp)}][{nameof(QueryTypes)}] Exception thrown when Dumping/Iterating managed assembly: {assembly}");
                        continue;
                    }
                    foreach (TypesDump.TypeIdentifiers type in typeIdentifiers)
                    {
                        // TODO: Filtering should probably be done in the Diver's side
                        if (matchesFilter(type.TypeName))
                            yield return new CandidateType(RuntimeType.Managed, type.TypeName, assembly);
                    }

                }
            }

            if (_unmanagedCommunicator != null)
            {
                _unmanagedModulesList ??=
                    _unmanagedCommunicator.DumpDomains().AvailableDomains.Single().AvailableModules;
                foreach (string module in _unmanagedModulesList)
                {
                    List<TypesDump.TypeIdentifiers> typeIdentifiers;
                    try
                    {
                        typeIdentifiers = _unmanagedCommunicator.DumpTypes(module).Types;
                    }
                    catch
                    {
                        // TODO:
                        Debug.WriteLine(
                            $"[{nameof(RemoteApp)}][{nameof(QueryTypes)}] Exception thrown when Dumping/Iterating unmanaged module: {module}");
                        continue;
                    }

                    foreach (TypesDump.TypeIdentifiers type in typeIdentifiers)
                    {
                        // TODO: Filtering should probably be done in the Diver's side
                        if (matchesFilter(type.TypeName))
                            yield return new CandidateType(RuntimeType.Unmanaged, type.TypeName, module);
                    }
                }
            }
        }

        public IEnumerable<CandidateObject> QueryInstances(CandidateType typeFilter, bool dumpHashcodes = true) => QueryInstances(typeFilter.TypeFullName, typeFilter.Runtime, dumpHashcodes);
        public IEnumerable<CandidateObject> QueryInstances(Type typeFilter, RuntimeType runtime = RuntimeType.Managed, bool dumpHashcodes = true) => QueryInstances(typeFilter.FullName, runtime, dumpHashcodes);

        /// <summary>
        /// Gets all object candidates for a specific filter
        /// </summary>
        /// <param name="typeFullNameFilter">Objects with Full Type Names of this EXACT string will be returned. You can use '*' as a "0 or more characters" wildcard</param>
        /// <param name="dumpHashcodes">Whether to also dump hashcodes of every matching object.
        /// This makes resolving the candidates later more reliable but for wide queries (e.g. "*") this might fail the entire search since it causes instabilities in the heap when examining it.
        /// </param>
        public IEnumerable<CandidateObject> QueryInstances(string typeFullNameFilter, RuntimeType runtime = RuntimeType.Managed, bool dumpHashcodes = true)
        {
            DiverCommunicator communicator =
                runtime == RuntimeType.Managed ? ManagedCommunicator : UnanagedCommunicator;

            var managedHeapDump = communicator.DumpHeap(typeFullNameFilter, dumpHashcodes);
            var managedCandidates = managedHeapDump.Objects.Select(heapObj => new CandidateObject(runtime, heapObj.Address, heapObj.Type, heapObj.HashCode));
            return managedCandidates;
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
        public Type GetRemoteType(string typeFullName, string assembly = null, RuntimeType runtime = RuntimeType.Managed)
        {
            if (runtime == RuntimeType.Managed)
            {
                // Easy case: Trying to resolve from cache or from local assemblies
                var resolver = TypesResolver.Instance;
                Type res = resolver.Resolve(assembly, typeFullName);
                if (res != null)
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
                RemoteTypesFactory rtf =
                    new RemoteTypesFactory(resolver, _managedCommunicator, avoidGenericsRecursion: true);
                var dumpedType = _managedCommunicator.DumpType(typeFullName, assembly);
                return rtf.Create(this, dumpedType);
            }
            else if (runtime == RuntimeType.Unmanaged)
            {
                throw new NotImplementedException();
            }

            throw new ArgumentException($"Runtime should only be {RuntimeType.Managed} or {RuntimeType.Unmanaged}");
        }
        /// <summary>
        /// Returns a handle to a remote type based on a given local type.
        /// </summary>
        public Type GetRemoteType(Type localType, RuntimeType runtime = RuntimeType.Managed) => GetRemoteType(localType.FullName, localType.Assembly.GetName().Name, runtime);
        public Type GetRemoteType(CandidateType candidate) => GetRemoteType(candidate.TypeFullName, candidate.Assembly, candidate.Runtime);
        internal Type GetRemoteType(ManagedTypeDump managedTypeDump) => GetRemoteType(managedTypeDump.Type, managedTypeDump.Assembly);

        /// <summary>
        /// Loads an assembly into the remote process
        /// </summary>
        public bool LoadManagedAssembly(Assembly assembly) => LoadManagedAssembly(assembly.Location);

        /// <summary>
        /// Loads an assembly into the remote process
        /// </summary>
        public bool LoadManagedAssembly(string path)
        {
            bool res = _managedCommunicator.InjectDll(path);
            if (res)
            {
                // Re-setting the cached domains because otherwise we won't
                // see our newly injected module
                _managedDomains = null;
            }
            return res;
        }

        /// <summary>
        /// Get a managed remote Enum
        /// </summary>
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

        public RemoteObject GetRemoteObject(CandidateObject candidate) => GetRemoteObject(candidate.Address, candidate.TypeFullName, candidate.HashCode, candidate.Runtime);
        public RemoteObject GetRemoteObject(ulong remoteAddress, string typeName, int? hashCode = null, RuntimeType runtime = RuntimeType.Managed)
        {
            if(runtime == RuntimeType.Managed)
                return _remoteObjects.GetRemoteObject(remoteAddress, typeName, hashCode);
            if (runtime == RuntimeType.Unmanaged)
                throw new NotImplementedException();
            throw new ArgumentException($"Runtime should only be {RuntimeType.Managed} or {RuntimeType.Unmanaged}");
        }

        //
        // IDisposable
        //
        public void Dispose()
        {
            ManagedCommunicator?.KillDiver();
            _managedCommunicator = null;
            _procWithDiver = null;
        }

    }
}