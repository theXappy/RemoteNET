using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Runtime.CompilerServices;
using RemoteNET.Internal;
using RemoteNET.Internal.Extensions;
using RemoteNET.Internal.Reflection;
using RemoteNET.Properties;
using ScubaDiver.API;
using ScubaDiver.API.Dumps;

namespace RemoteNET
{
    public class RemoteApp : IDisposable
    {
        private Process _procWithDiver;
        private DiverCommunicator _communicator;

        public RemoteActivator Activator { get; private set; }

        public DiverCommunicator Communicator => _communicator;

        private RemoteApp(Process procWithDiver, DiverCommunicator communicator)
        {
            _procWithDiver = procWithDiver;
            _communicator = communicator;
            Activator = new RemoteActivator(communicator, this);
        }

        public IEnumerable<CandidateObject> QueryInstances(Type typeFilter) => QueryInstances(typeFilter.FullName);
        public IEnumerable<CandidateObject> QueryInstances(string typeFullNameFilter)
        {
            return _communicator.DumpHeap(typeFullNameFilter).Objects.Select(heapObj => new CandidateObject(heapObj.Address, heapObj.Type));
        }

        public Type GetRemoteType(string typeFullName, string assembly = null)
        {
            RemoteTypesFactory rtf = new RemoteTypesFactory(TypesResolver.Instance);
            rtf.AllowOwnDumping(_communicator);
            var dumpedType = _communicator.DumpType(typeFullName, assembly);
            return rtf.Create(this, dumpedType);
        }

        public RemoteObject GetRemoteObject(CandidateObject candidate) => GetRemoteObject(candidate.Address);
        public RemoteObject GetRemoteObject(ulong remoteAddress)
        {
            ObjectDump od;
            TypeDump td;
            try
            {
                od = _communicator.DumpObject(remoteAddress, true);
                td = _communicator.DumpType(od.Type);
            }
            catch (Exception e)
            {
                throw new Exception("Could not dump remote object/type.", e);
            }
            

            var remoteObject = new RemoteObject(new RemoteObjectRef(od, td, _communicator), this);
            return remoteObject;
        }

        /// <summary>
        /// Creates a new provider.
        /// </summary>
        /// <param name="target">Process to create the provider for</param>
        /// <returns>A provider for the given process</returns>
        public static RemoteApp Connect(Process target)
        {
            // TODO: If target is our own process run a local Diver without DLL injections

            bool alreadyInjected = false;
            try
            {
                alreadyInjected = target.Modules.AsEnumerable()
                                        .Any(module => module.ModuleName.Contains("BootstrapDLL"));
            }
            catch
            {
                // Sometimes this happens because x32 vs x64 process interaction is not supported
            }

            // To make the Diver's port predictable even when re-attaching we'll derive it from the PID:
            ushort diverPort = (ushort) target.Id;

            if (!alreadyInjected)
            {
                // Dumping injector + bootstrap DLL to a temp dir
                var tempDirPath = Path.Combine(Path.GetTempPath(), typeof(RemoteApp).Assembly.GetName().Name);
                System.IO.DirectoryInfo tempDirInfo = new DirectoryInfo(tempDirPath);
                if (tempDirInfo.Exists)
                {
                    tempDirInfo.Delete(true);
                }
                tempDirInfo.Create();


                // Decide which injection toolkit to use x32 or x64
                string injectorPath = Path.Combine(tempDirPath, nameof(Resources.Injector)+".exe");
                string bootstrapPath = Path.Combine(tempDirPath, nameof(Resources.BootstrapDLL)+".dll");
                byte[] injectorResource = Resources.Injector;
                byte[] bootstrapDllResource = Resources.BootstrapDLL;
                if (target.Is64Bit())
                {
                    injectorPath = Path.Combine(tempDirPath, nameof(Resources.Injector_x64)+".exe");
                    bootstrapPath = Path.Combine(tempDirPath, nameof(Resources.BootstrapDLL_x64)+".dll");
                    injectorResource = Resources.Injector_x64;
                    bootstrapDllResource = Resources.BootstrapDLL_x64;
                }

                
                // Extract toolkit to disk
                File.WriteAllBytes(injectorPath, injectorResource);
                File.WriteAllBytes(bootstrapPath, bootstrapDllResource);

                // Unzip scuba diver and dependencies into their own directory
                var scubaDirInfo = tempDirInfo.CreateSubdirectory("Scuba");
                using (var diverZipMemoryStream = new MemoryStream(Resources.ScubaDiver))
                {
                    ZipArchive diverZip = new ZipArchive(diverZipMemoryStream);
                    // This extracts the "Scuba" directory from the zip to *tempDir*
                    diverZip.ExtractToDirectory(tempDirPath);
                }

                string scubaDiverDllPath = Directory.EnumerateFiles(scubaDirInfo.Name)
                    .Single(scubaFile => scubaFile.EndsWith("ScubaDiver.dll"));

                var startInfo = new ProcessStartInfo(injectorPath, $"{target.Id} {scubaDiverDllPath} {diverPort}");
                startInfo.WorkingDirectory = tempDirPath;
                startInfo.UseShellExecute = false;
                startInfo.RedirectStandardOutput = true;
                var injectorProc = Process.Start(startInfo);
                if (injectorProc != null && injectorProc.WaitForExit(5000))
                {
                    // Injector finished early, there's probably an error.
                    var stdout = injectorProc.StandardOutput.ReadToEnd();
                    Console.WriteLine("Error with injector. Raw STDOUT:\n" + stdout);
                    return null;
                }
                // TODO: Get results of injector
            }

            // TODO: Make it configurable
            string diverAddr = "127.0.0.1";
            DiverCommunicator com = new DiverCommunicator(diverAddr, diverPort);

            return new RemoteApp(target, com);
        }

        public void Dispose()
        {
            this.Communicator?.KillDiver();
            this._communicator = null;
            this._procWithDiver = null;
        }

    }
}