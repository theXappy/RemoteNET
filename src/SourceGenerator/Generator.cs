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


            // Create target with required DLLs
            Process victim = Process.Start(@"C:\git\rnet-kit\RemoteNET\src\Tests\TestTarget\bin\Debug\net7.0-windows\TestTarget.exe");

            List<string> targetDlls;
            try
            {
                targetDlls = File.ReadAllLines(inspectedDllsFilePath).Select(x => x.Trim()).ToList();
            }
            catch (Exception e)
            {
                Log($"Error reading target list: {e.Message}\n");
                Debug.WriteLine("Error reading target list: " + e.Message);
                return;
            }

            foreach (string dllPath in targetDlls)
            {
                Log("Injecting: {dllPath}");
                var injectCommand =
                    CliWrap.Cli.Wrap(@"C:\git\rnet-kit\rnet-inject\bin\Debug\net7.0-windows7.0\rnet-inject.exe")
                    .WithWorkingDirectory(@"C:\git\rnet-kit\rnet-inject\bin\Debug\net7.0-windows7.0\")
                    .WithArguments($"-u -t {victim.Id} -d {dllPath}")
                    .WithValidation(CliWrap.CommandResultValidation.None)
                    .ExecuteBufferedAsync();

                injectCommand.Task.Wait();
                Log($"rnet-inject STDERR: {injectCommand.Task.Result.StandardError}\n");
                Log($"rnet-inject STDOUT: {injectCommand.Task.Result.StandardOutput}\n");
            }

            var dumpCommand =
                CliWrap.Cli.Wrap(@"C:\git\rnet-kit\rnet-class-dump\bin\Debug\net7.0-windows\rnet-class-dump.exe")
                .WithArguments($"-u -t {victim.Id} -l {inspectedTypesFilePath}")
                .WithValidation(CliWrap.CommandResultValidation.None)
                .ExecuteBufferedAsync();

            dumpCommand.Task.Wait();
            Log($"rnet-class-dump STDERR: {dumpCommand.Task.Result.StandardError}\n");
            Log($"rnet-class-dump STDOUT: {dumpCommand.Task.Result.StandardOutput}\n");

            var compilation = context.Compilation;

            context.AddSource($"all_types.cs", SourceText.From("p00p", Encoding.UTF8));
            return;
        }
    }
}
