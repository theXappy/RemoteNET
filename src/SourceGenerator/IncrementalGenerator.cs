using System.Collections.Generic;
using System.Collections.Immutable;
using System.Text;
using System.Threading;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;
using System.IO;
using System.Diagnostics;
using RemoteNET;
using System;
using RemoteNET.Access;
using System.Linq;

namespace SourceGenerator
{
    [Generator]
	public class IncrementalGenerator : IIncrementalGenerator
	{
        private const string TargetListPath = "RemoteNET/src/SourceGenerator/MyProj_RemoteNET_Targets.txt";

        private List<string> targetDlls;
        private Process testTargetProcess;

		public void Initialize(IncrementalGeneratorInitializationContext context)
		{
            File.AppendAllText(@"C:\Users\Shai\AppData\Local\Temp\logz\a.txt", "Initialize started\n");
        }

        private void GenerateSource(SourceProductionContext context, ImmutableArray<ITypeSymbol> typeSymbols)
        {
            File.AppendAllText(@"C:\Users\Shai\AppData\Local\Temp\logz\a.txt", "GenerateSource started\n");

            if (testTargetProcess == null || testTargetProcess.HasExited)
            {
                File.AppendAllText(@"C:\Users\Shai\AppData\Local\Temp\logz\a.txt", "TestTarget process is not running\n");
                Debug.WriteLine("TestTarget process is not running.");
                return;
            }
        }

        private ITypeSymbol GetTypeSymbols(GeneratorSyntaxContext context, CancellationToken cancellationToken)
        {
            File.AppendAllText(@"C:\Users\Shai\AppData\Local\Temp\logz\a.txt", "GetTypeSymbols started\n");

            var decl = (ClassDeclarationSyntax)context.Node;
            File.AppendAllText(@"C:\Users\Shai\AppData\Local\Temp\logz\a.txt", "Retrieved class declaration\n");

            if (context.SemanticModel.GetDeclaredSymbol(decl, cancellationToken) is ITypeSymbol typeSymbol)
            {
                File.AppendAllText(@"C:\Users\Shai\AppData\Local\Temp\logz\a.txt", $"Declared symbol: {typeSymbol.Name}\n");
                return typeSymbol;
            }

            File.AppendAllText(@"C:\Users\Shai\AppData\Local\Temp\logz\a.txt", "No declared symbol found\n");
            return null;
        }
    }
}
