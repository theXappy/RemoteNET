using System.Diagnostics;
using System.Runtime.InteropServices;
using NUnit.Framework;
using ScubaDiver;
using ScubaDiver.Rtti;
using Windows.Win32;

#pragma warning disable CS8625
#pragma warning disable CS8618
#pragma warning disable CS8602

namespace RemoteNET.Tests
{
    [TestFixture]
    public class RttiScannerTests
    {
        private SafeHandle? _msvcpHandle;
        private SafeHandle? _libSpenHandle;

        [SetUp]
        public void Setup()
        {
            // Load dependencies first (MSVCP)
            var msvcpPath =
                @"C:\Program Files\WindowsApps\Microsoft.VCLibs.140.00_14.0.33519.0_x64__8wekyb3d8bbwe\msvcp140_app.dll";
            if (File.Exists(msvcpPath))
            {
                string dllDirectory = Path.GetDirectoryName(msvcpPath);
                if (!Windows.Win32.PInvoke.SetDllDirectory(dllDirectory))
                {
                    Console.WriteLine($"SetDllDirectory failed for: {dllDirectory} with error code: {Marshal.GetLastWin32Error()}");
                }

                _msvcpHandle = PInvoke.LoadLibrary(msvcpPath);
                if (_msvcpHandle.IsInvalid)
                {
                    Assert.Inconclusive($"Failed to load MSVCP dependency: {msvcpPath}");
                }
            }

            // Load the target DLL
            var libSpenPath = @"C:\Users\Shai\Desktop\SAM_NOTES_RES\inspection\libSpen_base.dll";
            if (!File.Exists(libSpenPath))
            {
                Assert.Inconclusive($"Test DLL not found: {libSpenPath}");
            }

            string libSpenDirectory = Path.GetDirectoryName(libSpenPath);
            if (!Windows.Win32.PInvoke.SetDllDirectory(libSpenDirectory))
            {
                Console.WriteLine($"SetDllDirectory failed for: {libSpenDirectory} with error code: {Marshal.GetLastWin32Error()}");
            }

            _libSpenHandle = PInvoke.LoadLibrary(libSpenPath);
            if (_libSpenHandle.IsInvalid)
            {
                var error = Marshal.GetLastWin32Error();
                Assert.Inconclusive($"Failed to load target DLL: {libSpenPath}, Win32Error: {error}");
            }
        }

        [TearDown]
        public void TearDown()
        {
            _libSpenHandle?.Dispose();
            _msvcpHandle?.Dispose();
        }

        [Test]
        public void TricksterWrapper_ShouldFindSPenUwpLogType()
        {
            // Arrange
            var tricksterWrapper = new TricksterWrapper();
            
            // Act - Force a refresh to pick up newly loaded modules
            tricksterWrapper.Refresh();
            
            // Get all modules that contain "libSpen" 
            var modules = tricksterWrapper.GetUndecoratedModules(name => name.Contains("libSpen", StringComparison.OrdinalIgnoreCase));
            
            // Assert we found the module
            Assert.That(modules, Is.Not.Empty, "Should find at least one libSpen module");
            
            var libSpenModule = modules.FirstOrDefault(m => m.Name.Contains("libSpen_base", StringComparison.OrdinalIgnoreCase));
            Assert.That(libSpenModule, Is.Not.Null, "Should find libSpen_base module specifically");
            
            Console.WriteLine($"Found module: {libSpenModule.Name}");
            Console.WriteLine($"Types in module: {libSpenModule.Types.Count()}");
            
            // List all types found
            foreach (var type in libSpenModule.Types)
            {
                Console.WriteLine($"  Found type: {type.FullTypeName}");
            }
            
            // Look specifically for SPen::UwpLog
            var uwpLogType = libSpenModule.Types.FirstOrDefault(t => 
                t.FullTypeName.Contains("SPen::UwpLog", StringComparison.OrdinalIgnoreCase) ||
                t.NamespaceAndName.Contains("SPen::UwpLog", StringComparison.OrdinalIgnoreCase));
            
            Assert.That(uwpLogType, Is.Not.Null, "Should find SPen::UwpLog type in libSpen_base module");
        }

        [Test]
        public void TricksterWrapper_ShouldNotCreateSPenClass()
        {
            // Arrange
            var tricksterWrapper = new TricksterWrapper();
            
            // Act - Force a refresh to pick up newly loaded modules
            tricksterWrapper.Refresh();
            
            // Get all modules that contain "libSpen" 
            var modules = tricksterWrapper.GetUndecoratedModules(name => name.Contains("libSpen", StringComparison.OrdinalIgnoreCase));
            
            if (!modules.Any())
            {
                Assert.Inconclusive("No libSpen modules found for this test");
            }
            
            var libSpenModule = modules.FirstOrDefault(m => m.Name.Contains("libSpen_base", StringComparison.OrdinalIgnoreCase));
            if (libSpenModule == null)
            {
                Assert.Inconclusive("libSpen_base module not found for this test");
            }
            
            // Act - Look for any type that is just "SPen" (without namespace qualifiers)
            var incorrectSPenTypes = libSpenModule.Types.Where(t => 
                string.Equals(t.FullTypeName, "SPen", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(t.NamespaceAndName, "SPen", StringComparison.OrdinalIgnoreCase) ||
                (t.FullTypeName.EndsWith("SPen", StringComparison.OrdinalIgnoreCase) && 
                 !t.FullTypeName.Contains("::", StringComparison.OrdinalIgnoreCase))).ToList();
            
            Console.WriteLine($"Found {incorrectSPenTypes.Count} types that might be incorrectly parsed as standalone 'SPen' class:");
            foreach (var type in incorrectSPenTypes)
            {
                Console.WriteLine($"  Suspicious type: '{type.FullTypeName}' (NamespaceAndName: '{type.NamespaceAndName}')");
            }
            
            // Assert - We should NOT find any standalone "SPen" class
            // SPen should always be a namespace (e.g., "SPen::SomeClass"), never a standalone class
            Assert.That(incorrectSPenTypes, Is.Empty, 
                "Should not create a standalone 'SPen' class. SPen should be a namespace containing other classes like 'SPen::UwpLog'.");
        }

        [Test]
        public void ExportsMaster_ShouldFindExportedSymbols()
        {
            // Test if the issue is with exports parsing
            var process = Process.GetCurrentProcess();
            var modules = process.Modules.Cast<ProcessModule>().ToList();
            
            var libSpenModule = modules.FirstOrDefault(m => 
                m.ModuleName.Contains("libSpen_base", StringComparison.OrdinalIgnoreCase));
            
            if (libSpenModule == null)
            {
                Assert.Inconclusive("libSpen_base module not loaded");
            }
            
            var moduleInfo = new ModuleInfo(
                libSpenModule.ModuleName, 
                (nuint)libSpenModule.BaseAddress, 
                (nuint)libSpenModule.ModuleMemorySize);
            
            var exportsMaster = new ExportsMaster();
            
            // Act
            exportsMaster.ProcessExports(moduleInfo);
            var exports = exportsMaster.GetExports(moduleInfo);
            var undecoratedExports = exportsMaster.GetUndecoratedExports(moduleInfo);
            
            Console.WriteLine($"Total exports in {moduleInfo.Name}: {exports.Count}");
            Console.WriteLine($"Undecorated exports: {undecoratedExports.Count}");
            
            // List some exports
            foreach (var export in exports.Take(20))
            {
                Console.WriteLine($"  Export: {export.Name}");
            }
            
            // Look for SPen-related exports
            var spenExports = undecoratedExports.Where(e => 
                e.UndecoratedFullName.Contains("SPen::", StringComparison.OrdinalIgnoreCase)).ToList();
                
            Console.WriteLine($"SPen-related undecorated exports: {spenExports.Count}");
            foreach (var spenExport in spenExports.Take(10))
            {
                Console.WriteLine($"  SPen export: {spenExport.UndecoratedFullName}");
            }
            
            // Look for UwpLog in exports
            var uwpLogExports = undecoratedExports.Where(e => 
                e.UndecoratedFullName.Contains("UwpLog", StringComparison.OrdinalIgnoreCase)).ToList();
                
            Console.WriteLine($"UwpLog-related exports: {uwpLogExports.Count}");
            foreach (var uwpLogExport in uwpLogExports)
            {
                Console.WriteLine($"  UwpLog export: {uwpLogExport.UndecoratedFullName}");
            }
            
            Assert.That(exports, Is.Not.Empty, "Should find some exports in the DLL");
        }
    }
}

#pragma warning restore CS8625
#pragma warning restore CS8618
#pragma warning restore CS8602