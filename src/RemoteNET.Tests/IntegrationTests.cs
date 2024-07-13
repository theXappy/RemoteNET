using CliWrap;
using CliWrap.Buffered;
using RemoteNET.Internal.Extensions;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using TestTarget;

namespace RemoteNET.Tests
{
    public class DisposableTarget : IDisposable
    {
        private readonly Process process;

        public DisposableTarget(string exePath)
        {
            while(Process.GetProcessesByName(Path.GetFileNameWithoutExtension(exePath)).Any())
            {
                Thread.Sleep(10);
            }

            var wrap = Cli.Wrap(exePath).ExecuteBufferedAsync();
            process = Process.GetProcessById(wrap.ProcessId);

            while(process.GetSupportedTargetFramework() == "native")
            {
                Thread.Sleep(10);
            }
            Debug.WriteLine($"[{nameof(DisposableTarget)}] Final detected framework = {process.GetSupportedTargetFramework()}");
            // Give a few more seconds for the runtime to load and reach Main.
            Thread.Sleep(TimeSpan.FromSeconds(5));
        }

        public Process Process => process;

        public void Dispose()
        {
            try { process?.Kill(true); } catch { }
            try { process?.WaitForExit(TimeSpan.FromSeconds(10)); } catch { }
        }
    }

    [TestFixture]
    internal class IntegrationTests
    {
        private Random r = new Random();
        private string TestTargetExe => Path.ChangeExtension(typeof(TestClass).Assembly.Location, "exe");


        [Test]
        public void ConnectRemoteApp()
        {
            // Arrange
            using var target = new DisposableTarget(TestTargetExe);

            // Act
            var app = RemoteAppFactory.Connect(target.Process, RuntimeType.Managed);

            // Assert
            Assert.IsNotNull(app);
            app.Dispose();
        }

        [TestCase(1)]
        [TestCase(2)]
        [TestCase(3)]
        [TestCase(4)]
        [NonParallelizable]
        public void CheckAlivenessRemoteApp(int sleepSeconds)
        {
            if(sleepSeconds > 1)
                Thread.Sleep(10);
            // Arrange
            using var target = new DisposableTarget(TestTargetExe);

            // Act
            var app = RemoteAppFactory.Connect(target.Process, RuntimeType.Managed);
            Thread.Sleep(TimeSpan.FromSeconds(sleepSeconds));
            bool alive = app.Communicator.CheckAliveness();

            // Assert
            Assert.True(alive);
            var unmanAppp = RemoteAppFactory.Connect(target.Process, RuntimeType.Unmanaged);
            app.Dispose();
            unmanAppp.Dispose();
        }

        [Test]
        public void FindObjectInHeap()
        {
            // Arrange
            using var target = new DisposableTarget(TestTargetExe);

            // Act
            var app = RemoteAppFactory.Connect(target.Process, RuntimeType.Managed);
            List<CandidateObject> candidates = app.QueryInstances("*").ToList();
            Debug.WriteLine("Candidates #1 count: " + candidates.Count);
            candidates = app.QueryInstances("*").ToList();
            Debug.WriteLine("Candidates #2 count: " + candidates.Count);
            IEnumerable<RemoteObject> objects = candidates.Select(c => app.GetRemoteObject(c));
            List<Type> types = objects.Select(o => o.GetRemoteType()).ToList();
            var ro = objects.Single();

            // Assert
            Assert.That(ro, Is.Not.Null);
            Assert.That(ro.GetType().Name, Is.EqualTo(typeof(TestClass).Name));
        }
    }
}
