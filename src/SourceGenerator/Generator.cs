using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SourceGenerator
{
    [Generator]
    public class Generator : ISourceGenerator
    {
        void Log(string l)
        {
            File.AppendAllText(@"C:\Users\Shai\AppData\Local\Temp\logz\a.txt", l);
        }

        public void Initialize(GeneratorInitializationContext context)
        {
            Log("============ Generator Initialize started\n");
        }

        public void Execute(GeneratorExecutionContext context)
        {
            // Cal Unsafe Executer with a try-catch and write the exception to the log
            try
            {
                UnsafeExecute(context);
            }
            catch (Exception e)
            {
                Log($"Error: {e.Message}\n");
                Log($"StackTrace: {e.StackTrace}\n");
                // If it's an aggregated exception, log all inner exceptions
                if (e is AggregateException aggEx)
                {
                    foreach (var innerEx in aggEx.InnerExceptions)
                    {
                        Log($"Inner Exception: {innerEx.Message}\n");
                        Log($"Inner StackTrace: {innerEx.StackTrace}\n");
                    }
                }
                else
                {
                    Log($"Exception: {e.Message}\n");
                    Log($"StackTrace: {e.StackTrace}\n");
                }

                return;
            }
        }

        public void UnsafeExecute(GeneratorExecutionContext context)
        {
            Log("============ Listing ADditional Files\n");
            foreach (var additionalFile in context.AdditionalFiles)
            {
                Log($"Additional File: {additionalFile.Path}\n");
            }
            string inspectedDllsFilePath = context.AdditionalFiles.FirstOrDefault(x => x.Path.EndsWith("InspectedDlls.txt"))?.Path;
            string inspectedTypesFilePath = context.AdditionalFiles.FirstOrDefault(x => x.Path.EndsWith("InspectedTypes.txt"))?.Path;
            if (inspectedDllsFilePath == null || inspectedTypesFilePath == null)
            {
                Log("InspectedDlls.txt or InspectedTypes.txt not found\n");
                return;
            }

            // Define cache folder and key file paths
            string appDataFolder = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            string cacheFolder = Path.Combine(appDataFolder, "RemoteNetSourceGenCache");
            string keyFilePath = Path.Combine(cacheFolder, "key.txt");
            string stdoutCachePath = Path.Combine(cacheFolder, "stdout.txt");
            // Ensure cache folder exists
            Directory.CreateDirectory(cacheFolder);
            
            //input file contents
            string inspectedDllsContent = File.ReadAllText(inspectedDllsFilePath);
            string inspectedTypesContent = File.ReadAllText(inspectedTypesFilePath);
            
            // Get rnet-class-dump.exe version
            string dumpExePath = @"C:\git\rnet-kit\rnet-class-dump\bin\Debug\net8.0\rnet-class-dump.exe";
            string dumpExeVersion = FileVersionInfo.GetVersionInfo(dumpExePath).FileVersion ?? "unknown";
            
            // Check if cache is valid
            string stdout = null;
            bool cacheHit = false;
            string currentKey = $"{inspectedDllsContent}{inspectedTypesContent}{dumpExeVersion}";
            if (File.Exists(keyFilePath) && File.Exists(stdoutCachePath))
            {
                string cachedKey = File.ReadAllText(keyFilePath);
                if (cachedKey == currentKey)
                {
                    Log("[!!!] Cache hit. Loading stdout from cache.\n");
                    stdout = File.ReadAllText(stdoutCachePath);
                    cacheHit = true;
                }
                else
                {
                    Log("[!!!] Cache MISS :( key mismatch\n");
                }
            }
            else
            {
                Log("[!!!] No cache yet.....\n");
            }

            if (!cacheHit)
            {
                Log(">> Cache MISS. Starting to spawn and analyze...\n");
                Log($"InspectedDlls: {inspectedDllsContent}\n");
                Log($"InspectedTypes: {inspectedTypesContent}\n");
                stdout = SpawnAndAnalyze(inspectedDllsFilePath, inspectedTypesFilePath);
                if (string.IsNullOrEmpty(stdout))
                    return;

                // Write the current key to the key file
                File.WriteAllText(keyFilePath, currentKey);
                // Write the stdout to the cache file
                File.WriteAllText(stdoutCachePath, stdout);
            }

            // Split STDOUT to LINES and then split every line by '|'
            string[][] generatedFiles = stdout.Split(new[] { Environment.NewLine }, StringSplitOptions.RemoveEmptyEntries)
                .Select(x => x.Split('|').Select(y => y.Trim()).ToArray())
                .ToArray();


            //var compilation = context.Compilation;

            Log(">> Starting to write files\n");

            Log(">> Adding Source all_finds.cs\n");
            context.AddSource($"all_finds.cs", SourceText.From($"// {generatedFiles.Length} num of lines", Encoding.UTF8));
            Log(">> Adding Source all_finds.cs -- added\n");
            foreach (string[] generatedFile in generatedFiles)
            {
                Log($">> Trying to add next line. Array size is {generatedFile.Length}, we need 2 items\n");
                Log($">> Trying to add next line. DUMPED: " + string.Join(", ", generatedFile) + "\n");
                string classFullNameNormalized = generatedFile[0].Replace('!', '_');
                string filePath = generatedFile[1];
                Log($">> Writing: {filePath}\n");
                context.AddSource($"{classFullNameNormalized}.cs", SourceText.From(File.ReadAllText(filePath), Encoding.UTF8));
            }
            Log(">> Done writing files\n");

            return;
        }

        private string SpawnAndAnalyze(string inspectedDllsFilePath, string inspectedTypesFilePath)
        {
            // Start TestTarget.exe process
            var victimStartInfo = new ProcessStartInfo
            {
                FileName = @"C:\git\rnet-kit\RemoteNET\src\Tests\TestTarget\bin\Debug\net8.0\TestTarget.exe",
                WorkingDirectory = @"C:\git\rnet-kit\RemoteNET\src\Tests\TestTarget\bin\Debug\net8.0",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };

            //Log the start of the process
            Log("Starting TestTarget.exe...\n");
            var victimProc = Process.Start(victimStartInfo);
            if (victimProc == null)
            {
                Log("Failed to start TestTarget.exe\n");
                return null;
            }

            List<string> targetDlls;
            try
            {
                targetDlls = File.ReadAllLines(inspectedDllsFilePath).Select(x => x.Trim()).ToList();
            }
            catch (Exception e)
            {
                Log($"Error reading target list: {e.Message}\n");
                Debug.WriteLine("Error reading target list: " + e.Message);
                victimProc.Kill();
                return null;
            }

            foreach (string dllPath in targetDlls)
            {
                Log($"GENERATOR Injecting: {dllPath}\n");
                var injectStartInfo = new ProcessStartInfo
                {
                    FileName = @"C:\git\rnet-kit\rnet-inject\bin\Debug\net8.0\rnet-inject.exe",
                    WorkingDirectory = @"C:\git\rnet-kit\rnet-inject\bin\Debug\net8.0\",
                    Arguments = $"-u -t {victimProc.Id} -d \"{dllPath}\"",
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true
                };
                var injectProc = Process.Start(injectStartInfo);
                if (injectProc == null)
                {
                    Log("Failed to start rnet-inject.exe\n");
                    victimProc.Kill();
                    return null;
                }
                injectProc.WaitForExit();
                string injectStdErr = injectProc.StandardError.ReadToEnd();
                string injectStdOut = injectProc.StandardOutput.ReadToEnd();
                Log($"rnet-inject STDERR: {injectStdErr}\n");
                Log($"rnet-inject STDOUT: {injectStdOut}\n");

                if (injectProc.ExitCode != 0)
                {
                    Log($"rnet-inject failed with exit code {injectProc.ExitCode}\n");
                    if (victimProc.HasExited)
                    {
                        Log($"Test Target is dead\n");
                        Log($"Test Target STDERR: {victimProc.StandardError.ReadToEnd()}\n");
                        Log($"Test Target STDOUT: {victimProc.StandardOutput.ReadToEnd()}\n");
                    }
                    else
                    {
                        Log($"Test Target is alive...\n");
                    }
                    victimProc.Kill();
                    return null;
                }
            }
            Log(">> All DLLs injected successfully\n");

            var dumpStartInfo = new ProcessStartInfo
            {
                FileName = @"C:\git\rnet-kit\rnet-class-dump\bin\Debug\net8.0\rnet-class-dump.exe",
                Arguments = $"-u -t {victimProc.Id} -l \"{inspectedTypesFilePath}\"",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };
            Log("Starting rnet-class-dump.exe...\n");
            var dumpProc = Process.Start(dumpStartInfo);
            if (dumpProc == null)
            {
                Log("Failed to start rnet-class-dump.exe\n");
                victimProc.Kill();
                return null;
            }
            dumpProc.WaitForExit();
            string dumpStdErr = dumpProc.StandardError.ReadToEnd();
            string stdout = dumpProc.StandardOutput.ReadToEnd();
            Log($"rnet-class-dump STDERR: {dumpStdErr}\n");
            Log($"rnet-class-dump STDOUT: {stdout}\n");

            // Try to cleanup dummy target
            try
            {
                if (!victimProc.HasExited)
                    victimProc.Kill();
            }
            catch (Exception)
            {
                // Don't really care
            }

            return stdout;
        }
    }
}
