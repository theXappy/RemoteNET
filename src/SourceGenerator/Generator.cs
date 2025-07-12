using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;
using RemoteNET;
using RemoteNET.Access;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;

namespace SourceGenerator
{
    [Generator]
    public class Generator : ISourceGenerator
    {
        public void Initialize(GeneratorInitializationContext context)
        {
            File.AppendAllText(@"C:\Users\Shai\AppData\Local\Temp\logz\a.txt", "============ Generator Initialize started\n");

            // Register a syntax receiver that will be created for each generation pass
            context.RegisterForSyntaxNotifications(() => new SyntaxReceiver());
        }

        public void Execute(GeneratorExecutionContext context)
        {
            File.AppendAllText(@"C:\Users\Shai\AppData\Local\Temp\logz\a.txt", "============ Listing ADditional Files\n");
            foreach (var additionalFile in context.AdditionalFiles)
            {
                File.AppendAllText(@"C:\Users\Shai\AppData\Local\Temp\logz\a.txt", $"Additional File: {additionalFile.Path}\n");
            }
            string TargetListPath = context.AdditionalFiles[0].Path;


            var targetDlls = new List<string>();
            try
            {
                string[] lines = File.ReadAllLines(TargetListPath);
                File.AppendAllText(@"C:\Users\Shai\AppData\Local\Temp\logz\a.txt", "Read target list\n");

                foreach (string line in lines)
                {
                    string trimmedLine = line.Trim();
                    if (!string.IsNullOrEmpty(trimmedLine))
                    {
                        targetDlls.Add(trimmedLine);
                        File.AppendAllText(@"C:\Users\Shai\AppData\Local\Temp\logz\a.txt", $"Added target DLL: {trimmedLine}\n");
                    }
                }
            }
            catch (Exception e)
            {
                File.AppendAllText(@"C:\Users\Shai\AppData\Local\Temp\logz\a.txt", $"Error reading target list: {e.Message}\n");
                Debug.WriteLine("Error reading target list: " + e.Message);
                return;
            }

            Process testTargetProcess = null;
            try
            {
                string testTargetPath = @"C:\git\rnet-kit\RemoteNET\src\Tests\TestTarget\bin\Debug\net7.0-windows\TestTarget.exe";
                testTargetProcess = Process.Start(testTargetPath);
                File.AppendAllText(@"C:\Users\Shai\AppData\Local\Temp\logz\a.txt", "Started TestTarget process\n");
                File.AppendAllText(@"C:\Users\Shai\AppData\Local\Temp\logz\a.txt", $"TestTarget PID: {testTargetProcess.Id}\n");
                Debugger.Launch();
            }
            catch (Exception e)
            {
                File.AppendAllText(@"C:\Users\Shai\AppData\Local\Temp\logz\a.txt", $"Error starting TestTarget: {e.Message}\n");
                Debug.WriteLine("Error starting TestTarget: " + e.Message);
                return;
            }


            RemoteApp remoteApp = null;
            try
            {
                remoteApp = RemoteAppFactory.Connect(testTargetProcess, RuntimeType.Unmanaged);
                File.AppendAllText(@"C:\Users\Shai\AppData\Local\Temp\logz\a.txt", "Connected to RemoteApp\n");

                UnmanagedRemoteApp unmanagedRemoteApp = remoteApp as UnmanagedRemoteApp;

                if (unmanagedRemoteApp == null)
                {
                    File.AppendAllText(@"C:\Users\Shai\AppData\Local\Temp\logz\a.txt", "Failed to cast RemoteApp to UnmanagedRemoteApp\n");
                    Debug.WriteLine("Failed to cast RemoteApp to UnmanagedRemoteApp.");
                    return;
                }

                foreach (string dllPath in targetDlls)
                {
                    try
                    {
                        bool injected = unmanagedRemoteApp.InjectDll(dllPath);
                        File.AppendAllText(@"C:\Users\Shai\AppData\Local\Temp\logz\a.txt", $"Injected DLL: {dllPath}, Success: {injected}\n");

                        if (!injected)
                        {
                            Debug.WriteLine("Failed to inject DLL: " + dllPath);
                        }
                    }
                    catch (Exception e)
                    {
                        File.AppendAllText(@"C:\Users\Shai\AppData\Local\Temp\logz\a.txt", $"Error injecting DLL: {dllPath} - {e.Message}\n");
                        Debug.WriteLine("Error injecting DLL: " + dllPath + " - " + e.Message);
                    }
                }

                IEnumerable<CandidateType> q = remoteApp.QueryTypes("*");
                File.AppendAllText(@"C:\Users\Shai\AppData\Local\Temp\logz\a.txt", "Queried types\n");

                List<Type> types = q.Take(5).Select(t => remoteApp.GetRemoteType(t)).ToList();
                File.AppendAllText(@"C:\Users\Shai\AppData\Local\Temp\logz\a.txt", "Retrieved remote types\n");

                var sb = new StringBuilder();
                foreach (Type type in types)
                {
                    if (type is null)
                        continue;

                    sb.AppendLine(type.FullName);
                    File.AppendAllText(@"C:\Users\Shai\AppData\Local\Temp\logz\a.txt", $"Type: {type.FullName}\n");
                }

                context.AddSource($"all_types0.cs", SourceText.From(sb.ToString(), Encoding.UTF8));
                File.AppendAllText(@"C:\Users\Shai\AppData\Local\Temp\logz\a.txt", "Added source\n");
            }
            catch (Exception e)
            {
                File.AppendAllText(@"C:\Users\Shai\AppData\Local\Temp\logz\a.txt", $"Error generating source: {e.Message}\n");
                Debug.WriteLine("Error generating source: " + e.Message);
            }
            finally
            {
                if (remoteApp != null)
                {
                    remoteApp.Dispose();
                    File.AppendAllText(@"C:\Users\Shai\AppData\Local\Temp\logz\a.txt", "Disposed RemoteApp\n");
                }
            }
















            // retreive the populated receiver 
            if (!(context.SyntaxReceiver is SyntaxReceiver receiver))
                return;

            var compilation = context.Compilation;

            // loop over the candidate fields, and keep the ones that are actually annotated
            var symbols = new List<ITypeSymbol>();
            foreach (var decl in receiver.ClassDeclarations)
            {
                var model = compilation.GetSemanticModel(decl.SyntaxTree);
                if (model.GetDeclaredSymbol(decl, context.CancellationToken) is ITypeSymbol symbol)
                {
                    symbols.Add(symbol);
                }
            }



            context.AddSource($"all_types.cs", SourceText.From("p00p", Encoding.UTF8));
        }
    }

    /// <summary>
    /// Created on demand before each generation pass
    /// </summary>
    class SyntaxReceiver : ISyntaxReceiver
    {
        public List<ClassDeclarationSyntax> ClassDeclarations { get; } = new List<ClassDeclarationSyntax>();

        /// <summary>
        /// Called for every syntax node in the compilation, we can inspect the nodes and save any information useful for generation
        /// </summary>
        public void OnVisitSyntaxNode(SyntaxNode syntaxNode)
        {
            // any field with at least one attribute is a candidate for property generation
            if (syntaxNode is ClassDeclarationSyntax decl)
            {
                ClassDeclarations.Add(decl);
            }
        }
    }
}
