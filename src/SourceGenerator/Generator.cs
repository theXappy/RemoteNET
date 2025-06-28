using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
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
            // Call Unsafe Executer with a try-catch and write the exception to the log
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
            List<string> additionalFilesPaths = context.AdditionalFiles.Select(x => x.Path).ToList();
            Action<string, SourceText> addSource = context.AddSource;
            BizLogic bizLogic = new BizLogic();
            bizLogic.UnsafeExecuteFromFilePaths(additionalFilesPaths, addSource, ReportError);
            return;

            void ReportError(string message)
            {
                var descriptor = new DiagnosticDescriptor(
                    id: "GEN0001",
                    title: "RemoteNET Source Generator Error",
                    messageFormat: "{0}",
                    category: "RemoteNETSourceGenerator",
                    DiagnosticSeverity.Error,
                    isEnabledByDefault: true);

                var diagnostic = Diagnostic.Create(descriptor, Location.None, message);
                context.ReportDiagnostic(diagnostic);
            }
        }
    }
}
