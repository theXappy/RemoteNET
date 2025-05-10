using CliWrap.Buffered;
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
            string dumpExePath = @"C:\git\rnet-kit\rnet-class-dump\bin\Debug\net8.0-windows\rnet-class-dump.exe";
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
            context.AddSource($"all_finds.cs", SourceText.From($"// {generatedFiles.Length} num of lines", Encoding.UTF8));
            foreach (string[] generatedFile in generatedFiles)
            {
                Log($">> Writing: {generatedFile}\n");
                string classFullNameNormalized = generatedFile[0].Replace('!', '_');
                string filePath = generatedFile[1];
                context.AddSource($"{classFullNameNormalized}.cs", SourceText.From(File.ReadAllText(filePath), Encoding.UTF8));
            }
            Log(">> Done writing files\n");

            return;
        }

        private string SpawnAndAnalyze(string inspectedDllsFilePath, string inspectedTypesFilePath)
        {
            // Create target with required DLLs
            var victim = CliWrap.Cli.Wrap(@"C:\git\rnet-kit\RemoteNET\src\Tests\TestTarget\bin\Debug\net8.0-windows\TestTarget.exe")
            .WithWorkingDirectory(@"C:\git\rnet-kit\RemoteNET\src\Tests\TestTarget\bin\Debug\net8.0-windows")
            .WithValidation(CliWrap.CommandResultValidation.None)
            .ExecuteBufferedAsync();

            List<string> targetDlls;
            try
            {
                targetDlls = File.ReadAllLines(inspectedDllsFilePath).Select(x => x.Trim()).ToList();
            }
            catch (Exception e)
            {
                Log($"Error reading target list: {e.Message}\n");
                Debug.WriteLine("Error reading target list: " + e.Message);
                return null;
            }

            foreach (string dllPath in targetDlls)
            {
                Log($"GENERATOR Injecting: {dllPath}\n");
                var injectCommand =
                    CliWrap.Cli.Wrap(@"C:\git\rnet-kit\rnet-inject\bin\Debug\net8.0-windows\rnet-inject.exe")
                    .WithWorkingDirectory(@"C:\git\rnet-kit\rnet-inject\bin\Debug\net8.0-windows\")
                    .WithArguments($"-u -t {victim.ProcessId} -d \"{dllPath}\"")
                    .WithValidation(CliWrap.CommandResultValidation.None)
                    .ExecuteBufferedAsync();

                injectCommand.Task.Wait();
                Log($"rnet-inject STDERR: {injectCommand.Task.Result.StandardError}\n");
                Log($"rnet-inject STDOUT: {injectCommand.Task.Result.StandardOutput}\n");

                if (injectCommand.Task.Result.ExitCode != 0)
                {
                    Log($"rnet-inject failed with exit code {injectCommand.Task.Result.ExitCode}\n");
                    // Print whether Test Target is dead or not + STDERR
                    if (victim.Task.Result.ExitCode != 0)
                    {
                        Log($"Test Target is dead\n");
                        Log($"Test Target STDERR: {victim.Task.Result.StandardError}\n");
                        Log($"Test Target STDOUT: {victim.Task.Result.StandardOutput}\n");
                    }
                    else
                    {
                        Log($"Test Target is alive...\n");
                    }
                    return null;
                }
            }

            var dumpCommand =
                CliWrap.Cli.Wrap(@"C:\git\rnet-kit\rnet-class-dump\bin\Debug\net8.0-windows\rnet-class-dump.exe")
                .WithArguments($"-u -t {victim.ProcessId} -l \"{inspectedTypesFilePath}\"")
                .WithValidation(CliWrap.CommandResultValidation.None)
                .ExecuteBufferedAsync();

            dumpCommand.Task.Wait();
            Log($"rnet-class-dump STDERR: {dumpCommand.Task.Result.StandardError}\n");
            string stdout = dumpCommand.Task.Result.StandardOutput;
            Log($"rnet-class-dump STDOUT: {stdout}\n");



            // Try to cleanup dummy target
            try
            {
                Process.GetProcessById(victim.ProcessId).Kill();
            }
            catch (Exception e)
            {
                // Don't really care
            }

            return stdout;
        }
    }
}
