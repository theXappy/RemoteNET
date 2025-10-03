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
            var msvcpPath = @"C:\Program Files\WindowsApps\Microsoft.VCLibs.140.00_14.0.33519.0_x64__8wekyb3d8bbwe\msvcp140_app.dll";
            if (File.Exists(msvcpPath))
            {
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
        public void TricksterDirectAccess_ShouldFindSPenUwpLogType()
        {
            // Arrange - Test the lower-level Trickster class directly
            var trickster = new Trickster(Process.GetCurrentProcess());
            
            // Act
            trickster.ScanTypes();
            var scannedTypes = trickster.ScannedTypes;
            
            // Find our module
            var libSpenModule = scannedTypes.Keys.FirstOrDefault(m => 
                m.ModuleInfo.Name.Contains("libSpen_base", StringComparison.OrdinalIgnoreCase));
            
            Assert.That(libSpenModule, Is.Not.Null, "Should find libSpen_base module in Trickster results");
            
            var typesInModule = scannedTypes[libSpenModule];
            Console.WriteLine($"Trickster found {typesInModule.Count} types in {libSpenModule.ModuleInfo.Name}");
            
            // List all found types
            foreach (var type in typesInModule)
            {
                Console.WriteLine($"  Trickster found type: {type.FullTypeName}");
            }
            
            // Look for SPen::UwpLog
            var uwpLogType = typesInModule.FirstOrDefault(t => 
                t.FullTypeName.Contains("SPen::UwpLog", StringComparison.OrdinalIgnoreCase));
            
            Assert.That(uwpLogType, Is.Not.Null, "Trickster should find SPen::UwpLog type");
        }

        [Test]
        public void RttiScanner_ManualScan_ShouldFindSPenTypes()
        {
            // Arrange - Test the even lower-level RttiScanner
            var process = Process.GetCurrentProcess();
            var modules = process.Modules.Cast<ProcessModule>().ToList();
            
            var libSpenModule = modules.FirstOrDefault(m => 
                m.ModuleName.Contains("libSpen_base", StringComparison.OrdinalIgnoreCase));
            
            Assert.That(libSpenModule, Is.Not.Null, "Should find libSpen_base in process modules");
            
            var moduleInfo = new ModuleInfo(
                libSpenModule.ModuleName, 
                (nuint)libSpenModule.BaseAddress, 
                (nuint)libSpenModule.ModuleMemorySize);
            
            var sections = ProcessModuleExtensions.ListSections(moduleInfo);
            var dataSections = sections.Where(s => 
                s.Name.ToUpper().Contains("DATA") || 
                s.Name.ToUpper().Contains("RTTI")).ToList();
            
            Console.WriteLine($"Module: {moduleInfo.Name}");
            Console.WriteLine($"Base Address: 0x{moduleInfo.BaseAddress:X}");
            Console.WriteLine($"Size: 0x{moduleInfo.Size:X}");
            Console.WriteLine($"Data sections found: {dataSections.Count}");
            
            foreach (var section in dataSections)
            {
                Console.WriteLine($"  Section: {section.Name} at 0x{section.BaseAddress:X}, size: 0x{section.Size:X}");
            }
            
            // Manual scan using RttiScanner
            var foundTypes = new List<string>();
            var processHandle = PInvoke.OpenProcess(Windows.Win32.System.Threading.PROCESS_ACCESS_RIGHTS.PROCESS_ALL_ACCESS, 
                true, (uint)process.Id);
            
            try
            {
                using (var scanner = new RttiScanner(processHandle, moduleInfo.BaseAddress, moduleInfo.Size, sections))
                {
                    // Scan each data section for RTTI information
                    foreach (var section in dataSections)
                    {
                        var sectionSize = (nuint)section.Size;
                        var sectionBase = (nuint)section.BaseAddress;
                        
                        // Try scanning with both 32-bit and 64-bit methods
                        for (nuint offset = 8; offset < sectionSize; offset += 8)
                        {
                            var address = sectionBase + offset;
                            var className64 = scanner.GetClassName64(address, sections);
                            if (!string.IsNullOrEmpty(className64))
                            {
                                foundTypes.Add(className64);
                                Console.WriteLine($"  Found type (64-bit): {className64}");
                            }
                        }
                    }
                }
            }
            finally
            {
                //processHandle.Dispose();
            }
            
            Console.WriteLine($"Total types found via manual scan: {foundTypes.Count}");
            
            // Look for SPen namespace types
            var spenTypes = foundTypes.Where(t => t.Contains("SPen::", StringComparison.OrdinalIgnoreCase)).ToList();
            Console.WriteLine($"SPen namespace types found: {spenTypes.Count}");
            
            foreach (var spenType in spenTypes)
            {
                Console.WriteLine($"  SPen type: {spenType}");
            }
            
            // Check for UwpLog specifically
            var uwpLogFound = foundTypes.Any(t => t.Contains("UwpLog", StringComparison.OrdinalIgnoreCase));
            if (!uwpLogFound)
            {
                Console.WriteLine("UwpLog type NOT found in manual scan. This suggests it may not have RTTI or may be in a different section.");
            }
            else
            {
                Console.WriteLine("UwpLog type WAS found in manual scan!");
            }
            
            // At minimum, we should find some types in this DLL
            Assert.That(foundTypes, Is.Not.Empty, "Should find at least some types in libSpen_base.dll");
        }

        [Test]
        public void ProcessModuleExtensions_ListSections_ShouldFindSections()
        {
            // Test the section listing functionality
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
            
            // Act
            var sections = ProcessModuleExtensions.ListSections(moduleInfo);
            
            // Assert
            Assert.That(sections, Is.Not.Empty, "Should find sections in the module");
            
            Console.WriteLine($"Sections in {moduleInfo.Name}:");
            foreach (var section in sections)
            {
                Console.WriteLine($"  {section.Name}: 0x{section.BaseAddress:X} (size: 0x{section.Size:X})");
            }
            
            // Should have typical PE sections
            var hasTextSection = sections.Any(s => s.Name.Contains(".text", StringComparison.OrdinalIgnoreCase));
            var hasDataSection = sections.Any(s => s.Name.Contains("data", StringComparison.OrdinalIgnoreCase));
            
            Assert.That(hasTextSection, Is.True, "Should have .text section");
            Console.WriteLine($"Has .text section: {hasTextSection}");
            Console.WriteLine($"Has data section: {hasDataSection}");
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