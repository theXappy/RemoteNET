using CliWrap;
using CliWrap.Buffered;
using RemoteNET.Internal.Extensions;
using System.Diagnostics;

namespace RemoteNET.Tests
{
    public class DisposableTarget : IDisposable
    {
        private readonly Process process;

        public DisposableTarget(string exePath)
        {
            var wrap = Cli.Wrap(exePath).ExecuteBufferedAsync();
            process = Process.GetProcessById(wrap.ProcessId);

            while (process.GetSupportedTargetFramework() == "native")
            {
                Thread.Sleep(10);
            }
            Debug.WriteLine($"[{nameof(DisposableTarget)}] Final detected framework = {process.GetSupportedTargetFramework()}");
            // Give a few more seconds for the runtime to load and reach Main.
            Thread.Sleep(TimeSpan.FromSeconds(3));
        }

        public Process Process => process;

        public void Dispose()
        {
            try { process?.Kill(true); } catch { }
            try { process?.WaitForExit(TimeSpan.FromSeconds(10)); } catch { }
        }
    }
}
