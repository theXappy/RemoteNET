using System;
using System.CodeDom.Compiler;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Reflection;
using System.Resources;
using RemoteObject.Internal;
using RemoteObject.Properties;
using ScubaDiver;

namespace RemoteObject
{
    public class RemoteObjectsProvider
    {
        private readonly Process _procWithDiver;
        private readonly DiverCommunicator _communicator;

        private RemoteObjectsProvider(Process procWithDiver, DiverCommunicator communicator)
        {
            _procWithDiver = procWithDiver;
            _communicator = communicator;
        }

        public List<HeapDump.HeapObject> QueryRemoteInstances(string typeFilter)
        {
            return _communicator.DumpHeap(typeFilter).Objects;
        }

        public RemoteObject CreateRemoteObject(ulong remoteAddress)
        {
            ObjectDump od;
            TypeDump td;
            try
            {
                od = _communicator.DumpObject(remoteAddress, true);
                td = _communicator.DumpType(od.Type);
            }
            catch(Exception e)
            {
                throw new Exception("Could not dump remote object/type.", e);
            }

            var remoteObject = new RemoteObject(new RemoteObjectRef(od, td, _communicator));
            return remoteObject;
        }

        public static RemoteObjectsProvider Create(Process target)
        {
            // Dumping injector + bootstrap DLL to a temp dir
            var tempDir = Path.Combine(Path.GetTempPath(), (new Random()).Next(10_000,int.MaxValue).ToString());
            Directory.CreateDirectory(tempDir);

            var injectorPath = Path.Combine(tempDir, "Injector.exe");
            var bootstrapPath = Path.Combine(tempDir, "BootstrapDLL.dll");
            File.WriteAllBytes(injectorPath, Resources.Injector);
            File.WriteAllBytes(bootstrapPath, Resources.BootstrapDLL);

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

            // TODO: Make it configurable
            string diverAddr = "127.0.0.1";
            int diverPort = 9977;
            DiverCommunicator com = new DiverCommunicator(diverAddr, diverPort);

            return new RemoteObjectsProvider(target, com);
        }

    }
}