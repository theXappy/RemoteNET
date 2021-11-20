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
            return _communicator.DumpHeap(typeFullNameFilter).Objects.Select(heapObj => new CandidateObject(heapObj.Address, heapObj.Type, heapObj.HashCode));
        }

        /// <summary>
        /// Gets a handle to a remote type (even ones from assemblies we aren't referencing/loading to the local process)
        /// </summary>
        /// <param name="typeFullName">Full name of the type to get. For example 'System.Xml.XmlDocument'</param>
        /// <param name="assembly">Optional short name of the assembly containing the type. For example 'System.Xml.ReaderWriter.dll'</param>
        /// <returns></returns>
        public Type GetRemoteType(string typeFullName, string assembly = null)
        {
            RemoteTypesFactory rtf = new RemoteTypesFactory(TypesResolver.Instance);
            rtf.AllowOwnDumping(_communicator);
            var dumpedType = _communicator.DumpType(typeFullName, assembly);
            return rtf.Create(this, dumpedType);
        }
        /// <summary>
        /// Returns a handle to a remote type based on a given local type.
        /// </summary>
        public Type GetRemoteType(Type localType) => GetRemoteType(localType.FullName, localType.Assembly.GetName().Name);

        public RemoteObject GetRemoteObject(CandidateObject candidate) => GetRemoteObject(candidate.Address, candidate.HashCode);
        public RemoteObject GetRemoteObject(ulong remoteAddress, int? hashCode = null)
        {
            ObjectDump od;
            TypeDump td;
            try
            {
                od = _communicator.DumpObject(remoteAddress, true, hashCode);
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
                // Dumping injector + bootstrap DLL to a %localappdata%\RemoteNET
                var remoteNetAppDataDir = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    typeof(RemoteApp).Assembly.GetName().Name);
                DirectoryInfo remoteNetAppDataDirInfo = new DirectoryInfo(remoteNetAppDataDir);
                if (!remoteNetAppDataDirInfo.Exists)
                {
                    remoteNetAppDataDirInfo.Create();
                }

                // Decide which injection toolkit to use x32 or x64
                string injectorPath = Path.Combine(remoteNetAppDataDir, nameof(Resources.Injector)+".exe");
                string bootstrapPath = Path.Combine(remoteNetAppDataDir, nameof(Resources.BootstrapDLL)+".dll");
                byte[] injectorResource = Resources.Injector;
                byte[] bootstrapDllResource = Resources.BootstrapDLL;
                if (target.Is64Bit())
                {
                    injectorPath = Path.Combine(remoteNetAppDataDir, nameof(Resources.Injector_x64)+".exe");
                    bootstrapPath = Path.Combine(remoteNetAppDataDir, nameof(Resources.BootstrapDLL_x64)+".dll");
                    injectorResource = Resources.Injector_x64;
                    bootstrapDllResource = Resources.BootstrapDLL_x64;
                }

                // Check if injector or bootstrap resources differ from copies on disk
                string injectorResourceHash = HashUtils.BufferSHA256(injectorResource);
                string injectorFileHash = File.Exists(injectorPath) ? HashUtils.FileSHA256(injectorPath) : String.Empty;
                if(injectorResourceHash != injectorFileHash)
                {
                    File.WriteAllBytes(injectorPath, injectorResource);
                }
                string bootstrapResourceHash = HashUtils.BufferSHA256(bootstrapDllResource);
                string bootstrapFileHash = File.Exists(bootstrapPath) ? HashUtils.FileSHA256(bootstrapPath) : String.Empty;
                if (bootstrapResourceHash != bootstrapFileHash)
                {
                    File.WriteAllBytes(bootstrapPath, bootstrapDllResource);
                }

                // Unzip scuba diver and dependencies into their own directory
                var scubaDestDirInfo = new DirectoryInfo(
                                                Path.Combine(
                                                    remoteNetAppDataDir,
                                                    "Scuba")
                                                );
                if(!scubaDestDirInfo.Exists)
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
                using (var diverZipMemoryStream = new MemoryStream(Resources.ScubaDiver))
                {
                    ZipArchive diverZip = new ZipArchive(diverZipMemoryStream);
                    // This extracts the "Scuba" directory from the zip to *tempDir*
                    diverZip.ExtractToDirectory(tempDir);
                }

                // Going over unzipped files and checking which of those we need to copy to our AppData directory
                tempDirInfo = new DirectoryInfo(Path.Combine(tempDir,"Scuba"));
                foreach (FileInfo fileInfo in tempDirInfo.GetFiles())
                {
                    string destPath = Path.Combine(scubaDestDirInfo.FullName, fileInfo.Name);
                    if(File.Exists(destPath))
                    {
                        string dumpedFileHash = HashUtils.FileSHA256(fileInfo.FullName);
                        string previousFileHash = HashUtils.FileSHA256(destPath);
                        if(dumpedFileHash == previousFileHash)
                        {
                            // Skipping file because the previous version of it has the same hash
                            continue;
                        }
                    }
                    // Moving file to our AppData directory
                    File.Delete(destPath);
                    fileInfo.MoveTo(destPath);
                }


                // We are done with our temp directory
                tempDirInfo.Delete(recursive: true);

                string scubaDiverDllPath = scubaDestDirInfo.EnumerateFiles()
                    .Single(scubaFile => scubaFile.Name.EndsWith("ScubaDiver.dll")).FullName;

                var startInfo = new ProcessStartInfo(injectorPath, $"{target.Id} {scubaDiverDllPath} {diverPort}");
                startInfo.WorkingDirectory = remoteNetAppDataDir;
                startInfo.UseShellExecute = false;
                startInfo.RedirectStandardOutput = true;
                var injectorProc = Process.Start(startInfo);
                // TODO: Currently I allow 500ms for the injector to fail (indicated by exiting)
                if (injectorProc != null && injectorProc.WaitForExit(500))
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