using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using RemoteObject.Internal;
using RemoteObject.Internal.Extensions;
using RemoteObject.Internal.Reflection;
using RemoteObject.Properties;
using ScubaDiver;
using ScubaDiver.API;

namespace RemoteObject
{
    public class RemoteApp
    {
        private readonly Process _procWithDiver;
        private readonly DiverCommunicator _communicator;

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
            return rtf.Create(dumpedType);
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
            bool alreadyInjected = target.Modules.AsEnumerable().Any(module => module.ModuleName.Contains("BootstrapDLL"));

            if (!alreadyInjected)
            {
                // Dumping injector + bootstrap DLL to a temp dir
                var tempDir = Path.Combine(Path.GetTempPath(), (new Random()).Next(10_000, int.MaxValue).ToString());
                Directory.CreateDirectory(tempDir);


                // Decide which injection toolkit to use x32 or x64
                string injectorPath = Path.Combine(tempDir, "Injector.exe");
                string bootstrapPath = Path.Combine(tempDir, "BootstrapDLL.dll");
                byte[] injectorResource = Resources.Injector;
                byte[] bootstrapDllResource = Resources.BootstrapDLL;
                if (target.Is64Bit())
                {
                    injectorPath = Path.Combine(tempDir, "Injector64.exe");
                    bootstrapPath = Path.Combine(tempDir, "BootstrapDLL64.dll");
                    injectorResource = Resources.Injector64;
                    bootstrapDllResource = Resources.BootstrapDLL64;
                }

                // Extract toolkit to disk
                File.WriteAllBytes(injectorPath, injectorResource);
                File.WriteAllBytes(bootstrapPath, bootstrapDllResource);

                // Unzip scuba diver and dependencies into their own directory
                var scubaPath = Path.Combine(tempDir, "Scuba");
                Directory.CreateDirectory(scubaPath);
                using (var diverZipMemoryStream = new MemoryStream(Resources.ScubaDiver))
                {
                    ZipArchive diverZip = new ZipArchive(diverZipMemoryStream);
                    // This extracts the "Scuba" directory from the zip to *tempDir*
                    diverZip.ExtractToDirectory(tempDir);
                }

                var startInfo = new ProcessStartInfo(injectorPath, $"{target.Id}");
                startInfo.WorkingDirectory = tempDir;
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
            int diverPort = 9977;
            DiverCommunicator com = new DiverCommunicator(diverAddr, diverPort);

            return new RemoteApp(target, com);
        }

    }
}