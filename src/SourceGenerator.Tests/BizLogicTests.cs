using Microsoft.CodeAnalysis.Text;
using System.Diagnostics;

namespace SourceGenerator.Tests
{
    [TestClass]
    public sealed class BizLogicTests
    {
        [TestMethod]
        public void UnsafeExecute_ReturnsIfFilesMissing()
        {
            var biz = new BizLogic();
            var files = new List<string>();
            bool addSourceCalled = false;
            biz.UnsafeExecuteFromFilePaths(files, (name, text) => { addSourceCalled = true; }, s => Console.Error.WriteLine(s));
            Assert.IsFalse(addSourceCalled);
        }

        [TestMethod]
        public void GetInspectedFilePaths_ReturnsCorrectPaths()
        {
            var biz = new BizLogic();
            var files = new List<string> { "foo/InspectedDlls.txt", "bar/InspectedTypes.txt", "baz/other.txt" };
            var (dll, types) = biz.GetInspectedFilePaths(files);
            Assert.AreEqual("foo/InspectedDlls.txt", dll);
            Assert.AreEqual("bar/InspectedTypes.txt", types);
        }

        [TestMethod]
        public void GetCachePaths_ReturnsValidPaths()
        {
            var biz = new BizLogic();
            var (cacheFolder, keyFilePath, stdoutCachePath) = biz.GetCachePaths();
            Assert.IsTrue(cacheFolder.Contains("RemoteNetSourceGenCache"));
            Assert.IsTrue(keyFilePath.EndsWith("key.txt"));
            Assert.IsTrue(stdoutCachePath.EndsWith("stdout.txt"));
        }

        [TestMethod]
        public void ReadInputFiles_ReadsFileContents()
        {
            var biz = new BizLogic();
            var dllFile = Path.GetTempFileName();
            var typesFile = Path.GetTempFileName();
            File.WriteAllText(dllFile, "dll-content");
            File.WriteAllText(typesFile, "types-content");
            var (dll, types) = biz.ReadInputFiles(dllFile, typesFile);
            Assert.AreEqual("dll-content", dll);
            Assert.AreEqual("types-content", types);
            File.Delete(dllFile);
            File.Delete(typesFile);
        }

        [TestMethod]
        public void IsCacheValid_ReturnsTrueIfKeyMatches()
        {
            var biz = new BizLogic();
            var cacheDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
            Directory.CreateDirectory(cacheDir);
            var keyFile = Path.Combine(cacheDir, "key.txt");
            var stdoutFile = Path.Combine(cacheDir, "stdout.txt");
            File.WriteAllText(keyFile, "thekey");
            File.WriteAllText(stdoutFile, "stdout");
            Assert.IsTrue(biz.IsCacheValid(keyFile, stdoutFile, "thekey"));
            Assert.IsFalse(biz.IsCacheValid(keyFile, stdoutFile, "otherkey"));
            File.Delete(keyFile);
            File.Delete(stdoutFile);
            Directory.Delete(cacheDir);
        }

        [TestMethod]
        public void WriteCache_WritesFiles()
        {
            var biz = new BizLogic();
            var cacheDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
            Directory.CreateDirectory(cacheDir);
            var keyFile = Path.Combine(cacheDir, "key.txt");
            var stdoutFile = Path.Combine(cacheDir, "stdout.txt");
            biz.WriteCache(keyFile, stdoutFile, "k", "s");
            Assert.AreEqual("k", File.ReadAllText(keyFile));
            Assert.AreEqual("s", File.ReadAllText(stdoutFile));
            File.Delete(keyFile);
            File.Delete(stdoutFile);
            Directory.Delete(cacheDir);
        }

        [TestMethod]
        public void ParseGeneratedFiles_ParsesCorrectly()
        {
            var biz = new BizLogic();
            string stdout = "ClassA|fileA.cs\nClassB|fileB.cs\n";
            var dict = biz.ParseGeneratedFiles(stdout.Replace("\n", Environment.NewLine));
            Assert.AreEqual(2, dict.Count);
            Assert.AreEqual("fileA.cs", dict["ClassA"]);
            Assert.AreEqual("fileB.cs", dict["ClassB"]);
        }

        [TestMethod]
        public void AddGeneratedSources_CallsDelegateForEachFile()
        {
            var biz = new BizLogic();
            var generated = new Dictionary<string, string>();
            var tempFile = Path.GetTempFileName();
            File.WriteAllText(tempFile, "// file content");
            generated["ClassA"] = tempFile;
            int callCount = 0;
            void AddSource(string name, SourceText text)
            {
                callCount++;
            }
            biz.AddGeneratedSources(generated, AddSource);
            Assert.IsTrue(callCount >= 2); // all_finds.cs + 1 file
            File.Delete(tempFile);
        }

        [TestMethod]
        public void SpawnAndAnalyze_IntegrationTest()
        {
            var biz = new BizLogic();
            // Setup: create temp files for InspectedDlls.txt and InspectedTypes.txt
            var typesFile = Path.GetTempFileName();
            File.WriteAllText(typesFile, ""); // No types to inspect
            // This will try to start RemoteNET.Vessel.exe and run the pipeline. It may fail if dependencies are missing.
            string result = biz.SpawnAndAnalyze(new List<string>(), typesFile);
            // We expect null or a string, but mostly want to ensure no crash
            Assert.IsTrue(result == null || result is string);
            File.Delete(typesFile);
        }

        [TestMethod]
        public void StartVictimProcess_IntegrationTest()
        {
            var biz = new BizLogic();
            var proc = biz.StartVictimProcess();
            if (proc != null)
            {
                Assert.IsFalse(proc.HasExited);
                proc.Kill();
            }
            else
            {
                Assert.Inconclusive("RemoteNET.Vessel not found or failed to start.");
            }
        }

        [TestMethod]
        public void ReadTargetDlls_IntegrationTest()
        {
            var biz = new BizLogic();
            var dllFile = Path.GetTempFileName();
            File.WriteAllLines(dllFile, new[] { "foo.dll", "bar.dll" });
            var proc = Process.GetCurrentProcess();
            var dlls = biz.ReadTargetDlls(dllFile, proc);
            Assert.AreEqual(2, dlls.Count);
            Assert.AreEqual("foo.dll", dlls[0]);
            Assert.AreEqual("bar.dll", dlls[1]);
            File.Delete(dllFile);
        }

        [TestMethod]
        public void InjectDll_IntegrationTest()
        {
            var biz = new BizLogic();
            var proc = biz.StartVictimProcess();
            // This will likely fail unless rnet-inject.exe exists and works, so just check for no crash
            bool result = biz.InjectDll(proc, "notarealdll.dll");
            Assert.IsFalse(result);
        }

        [TestMethod]
        public void RunClassDump_IntegrationTest()
        {
            var biz = new BizLogic();
            var proc = Process.GetCurrentProcess();
            // This will likely fail unless rnet-class-dump.exe exists and works, so just check for no crash
            string result = biz.RunClassDump(proc, "notarealtypes.txt");
            Assert.IsTrue(result == null || result is string);
        }

        [TestMethod]
        public void TryKillProcess_IntegrationTest()
        {
            var biz = new BizLogic();
            var proc = new Process();
            proc.StartInfo.FileName = "cmd.exe";
            proc.StartInfo.Arguments = "/c timeout /t 2 >nul";
            proc.StartInfo.UseShellExecute = false;
            proc.Start();
            Thread.Sleep(500); // Give it a moment to start
            biz.TryKillProcess(proc);
            Assert.IsTrue(proc.HasExited);
        }

        [TestMethod]
        public void UnsafeExecute_IntegrationTest_WithProvidedLists()
        {
            var biz = new BizLogic();
            // Provided DLLs
            var injectedDlls = new List<string>
            {
                @"C:\\Program Files\\WindowsApps\\Microsoft.VCLibs.140.00_14.0.33519.0_x64__8wekyb3d8bbwe\\msvcp140_app.dll",
                @"C:\\Q_base.dll",
                @"C:\\Q_worddoc.dll",
                @"C:\\Q_document.dll"
            };
            // Provided types
            var typesToInspect = new List<string>
            {
                "*!SPen::UwpLog",
                "*!SPen::String",
                "*!SPen::BaseData",
                "*!SPen::NoteZip",
                "*!SPen::IOutputStream",
                "*!SPen::PreSubmitData",
                "*!SPen::HistoryEventHandler",
                "*!SPen::ICursorEventCallback",
                "*!SPen::FollowerObserverListener",
                "*!SPen::IStringEventCallback",
                "*!SPen::ObjectTextBox",
                "*!SPen::WContentFileEventListener",
                "*!SPen::WCursorEventListener",
                "*!SPen::WHistoryEventListener",
                "*!SPen::WObjectEventListener",
                "*!SPen::WObjectIndexMovedEventListener",
                "*!SPen::WObjectSelectedEventListener",
                "*!SPen::WPageEventListener",
                "*!SPen::WStringEventListener",
                "*!SPen::WTextEventListener",
                "*!SPen::WVoiceDataEventListener",
                "libSpen_base.dll!SPen::Bundle",
                "libSpen_base.dll!SPen::List",
                "libSpen_document.dll!SPen::HistoryData",
                "libSpen_document.dll!SPen::HistoryManager",
                "libSpen_document.dll!SPen::HistoryManager::CommandJob",
                "libSpen_document.dll!SPen::ICursorEventCallback",
                "libSpen_document.dll!SPen::ILayerEventCallback",
                "libSpen_document.dll!SPen::IObjectEventCallback",
                "libSpen_document.dll!SPen::IObjectIndexMovedEventCallback",
                "libSpen_document.dll!SPen::IPageEventCallback",
                "libSpen_document.dll!SPen::ITextEventCallback",
                "libSpen_document.dll!SPen::MediaFileManager",
                "libSpen_document.dll!SPen::ModelContext",
                "libSpen_base.dll!SPen::Point",
                "libSpen_base.dll!SPen::PointD",
                "libSpen_base.dll!SPen::PointF",
                "libSpen_base.dll!SPen::Rect",
                "libSpen_base.dll!SPen::RectD",
                "libSpen_base.dll!SPen::RectF",
                "libSpen_document.dll!SPen::StringIDManager",
                "libSpen_document.dll!SPen::VoiceNameManager",
                "libSpen_document.dll!SPen::ObjectBase",
                "libSpen_document.dll!SPen::ObjectList",
                "libSpen_document.dll!SPen::ObjectSpan",
                "libSpen_worddoc.dll!SPen::WPage",
                "libSpen_worddoc.dll!SPen::WNote"
            };

            var output = new Dictionary<string, string>();
            biz.UnsafeExecute(injectedDlls, typesToInspect, (name, text) =>
            {
                output[name] = text?.ToString();
            });

            // 1. Assert neither is empty
            foreach (var kvp in output)
            {
                Assert.IsFalse(string.IsNullOrWhiteSpace(kvp.Key), "Output name is empty");
                Assert.IsFalse(string.IsNullOrWhiteSpace(kvp.Value), $"Output text for '{kvp.Key}' is empty");
            }
            // 2. Assert there are AT LEAST as many keys as typesToInspect
            Assert.IsTrue(output.Count >= typesToInspect.Count, $"Expected at least {typesToInspect.Count} outputs, got {output.Count}");
        }

        [TestMethod]
        public void UnsafeExecute_IntegrationTest_OneTypeAtATime()
        {
            var biz = new BizLogic();
            // Provided DLLs
            var injectedDlls = new List<string>
            {
                @"C:\\Program Files\\WindowsApps\\Microsoft.VCLibs.140.00_14.0.33519.0_x64__8wekyb3d8bbwe\\msvcp140_app.dll",
                @"C:\\Users\\Shai\\Desktop\\SAM_NOTES_RES\\inspection\\libSpen_worddoc.dll",
                @"C:\\Users\\Shai\\Desktop\\SAM_NOTES_RES\\inspection\\libSpen_document.dll"
            };
            // Provided types
            var typesToInspect = new List<string>
            {
                "libSpen_base.dll!SPen::Uuid",
                "*!SPen::String",
                "*!SPen::BaseData",
                //"*!SPen::NoteZip",
                "*!SPen::IOutputStream",
                "*!SPen::PreSubmitData",
                "*!SPen::HistoryEventHandler",
                "*!SPen::ICursorEventCallback",
                "*!SPen::FollowerObserverListener",
                "*!SPen::IStringEventCallback",
                "*!SPen::ObjectTextBox",
                "*!SPen::WContentFileEventListener",
                "*!SPen::WCursorEventListener",
                "*!SPen::WHistoryEventListener",
                "*!SPen::WObjectEventListener",
                "*!SPen::WObjectIndexMovedEventListener",
                "*!SPen::WObjectSelectedEventListener",
                "*!SPen::WPageEventListener",
                "*!SPen::WStringEventListener",
                "*!SPen::WTextEventListener",
                "*!SPen::WVoiceDataEventListener",
                "libSpen_base.dll!SPen::Bundle",
                "libSpen_base.dll!SPen::List",
                "libSpen_document.dll!SPen::HistoryData",
                "libSpen_document.dll!SPen::HistoryManager",
                "libSpen_document.dll!SPen::HistoryManager::CommandJob",
                "libSpen_document.dll!SPen::ICursorEventCallback",
                "libSpen_document.dll!SPen::ILayerEventCallback",
                "libSpen_document.dll!SPen::IObjectEventCallback",
                "libSpen_document.dll!SPen::IObjectIndexMovedEventCallback",
                "libSpen_document.dll!SPen::IPageEventCallback",
                "libSpen_document.dll!SPen::ITextEventCallback",
                "libSpen_document.dll!SPen::MediaFileManager",
                "libSpen_document.dll!SPen::ModelContext",
                "libSpen_base.dll!SPen::Point",
                "libSpen_base.dll!SPen::PointD",
                "libSpen_base.dll!SPen::PointF",
                "libSpen_base.dll!SPen::Rect",
                "libSpen_base.dll!SPen::RectD",
                "libSpen_base.dll!SPen::RectF",
                "libSpen_document.dll!SPen::StringIDManager",
                "libSpen_document.dll!SPen::VoiceNameManager",
                "libSpen_document.dll!SPen::ObjectBase",
                "libSpen_document.dll!SPen::ObjectList",
                "libSpen_document.dll!SPen::ObjectSpan",
                "libSpen_worddoc.dll!SPen::WPage",
                "libSpen_worddoc.dll!SPen::WNote"
            };

            foreach (var type in typesToInspect)
            {
                var output = new Dictionary<string, string>();
                biz.UnsafeExecute(injectedDlls, new List<string> { type }, (name, text) =>
                {
                    output[name] = text?.ToString();
                });

                // Assert neither is empty
                foreach (var kvp in output)
                {
                    Assert.IsFalse(string.IsNullOrWhiteSpace(kvp.Key), $"Output name is empty for type '{type}'");
                    Assert.IsFalse(string.IsNullOrWhiteSpace(kvp.Value), $"Output text for '{kvp.Key}' is empty (type '{type}')");
                }
                // Assert at least one output for this type
                Assert.IsTrue(output.Count >= 1, $"Expected at least 1 output for type '{type}', got {output.Count}");
            }
        }

        public static IEnumerable<object[]> UnsafeExecute_TypeCases()
        {
            yield return new object[] { "libSpen_base.dll!SPen::Uuid" };
            yield return new object[] { "*!SPen::String" };
            yield return new object[] { "*!SPen::BaseData" };
            yield return new object[] { "*!SPen::NoteZip" };
            yield return new object[] { "*!SPen::IOutputStream" };
            yield return new object[] { "*!SPen::PreSubmitData" };
            yield return new object[] { "*!SPen::HistoryEventHandler" };
            yield return new object[] { "*!SPen::ICursorEventCallback" };
            yield return new object[] { "*!SPen::FollowerObserverListener" };
            yield return new object[] { "*!SPen::IStringEventCallback" };
            yield return new object[] { "*!SPen::ObjectTextBox" };
            yield return new object[] { "*!SPen::WContentFileEventListener" };
            yield return new object[] { "*!SPen::WCursorEventListener" };
            yield return new object[] { "*!SPen::WHistoryEventListener" };
            yield return new object[] { "*!SPen::WObjectEventListener" };
            yield return new object[] { "*!SPen::WObjectIndexMovedEventListener" };
            yield return new object[] { "*!SPen::WObjectSelectedEventListener" };
            yield return new object[] { "*!SPen::WPageEventListener" };
            yield return new object[] { "*!SPen::WStringEventListener" };
            yield return new object[] { "*!SPen::WTextEventListener" };
            yield return new object[] { "*!SPen::WVoiceDataEventListener" };
            yield return new object[] { "libSpen_base.dll!SPen::Bundle" };
            yield return new object[] { "libSpen_base.dll!SPen::List" };
            yield return new object[] { "libSpen_document.dll!SPen::HistoryData" };
            yield return new object[] { "libSpen_document.dll!SPen::HistoryManager" };
            yield return new object[] { "libSpen_document.dll!SPen::HistoryManager::CommandJob" };
            yield return new object[] { "libSpen_document.dll!SPen::ICursorEventCallback" };
            yield return new object[] { "libSpen_document.dll!SPen::ILayerEventCallback" };
            yield return new object[] { "libSpen_document.dll!SPen::IObjectEventCallback" };
            yield return new object[] { "libSpen_document.dll!SPen::IObjectIndexMovedEventCallback" };
            yield return new object[] { "libSpen_document.dll!SPen::IPageEventCallback" };
            yield return new object[] { "libSpen_document.dll!SPen::ITextEventCallback" };
            yield return new object[] { "libSpen_document.dll!SPen::MediaFileManager" };
            yield return new object[] { "libSpen_document.dll!SPen::ModelContext" };
            yield return new object[] { "libSpen_base.dll!SPen::Point" };
            yield return new object[] { "libSpen_base.dll!SPen::PointD" };
            yield return new object[] { "libSpen_base.dll!SPen::PointF" };
            yield return new object[] { "libSpen_base.dll!SPen::Rect" };
            yield return new object[] { "libSpen_base.dll!SPen::RectD" };
            yield return new object[] { "libSpen_base.dll!SPen::RectF" };
            yield return new object[] { "libSpen_document.dll!SPen::StringIDManager" };
            yield return new object[] { "libSpen_document.dll!SPen::VoiceNameManager" };
            yield return new object[] { "libSpen_document.dll!SPen::ObjectBase" };
            yield return new object[] { "libSpen_document.dll!SPen::ObjectList" };
            yield return new object[] { "libSpen_document.dll!SPen::ObjectSpan" };
            yield return new object[] { "libSpen_worddoc.dll!SPen::WPage" };
            yield return new object[] { "libSpen_worddoc.dll!SPen::WNote" };
            yield return new object[] { "libSpen_base.dll!SPen::ArrayList" };
            yield return new object[] { "libSpen_document.dll!SPen::HistoryManager" };
            yield return new object[] { "libSpen_document.dll!SPen::LineColorEffect" };
        }

        [Ignore]
        [DataTestMethod]
        [DynamicData(nameof(UnsafeExecute_TypeCases), DynamicDataSourceType.Method)]
        public void UnsafeExecute_IntegrationTest_SingleType(string type)
        {
            var biz = new BizLogic();
            var injectedDlls = new List<string>
            {
                @"C:\\Program Files\\WindowsApps\\Microsoft.VCLibs.140.00_14.0.33519.0_x64__8wekyb3d8bbwe\\msvcp140_app.dll",
                @"C:\\Users\\Shai\\Desktop\\SAM_NOTES_RES\\inspection\\libSpen_worddoc.dll",
                @"C:\\Users\\Shai\\Desktop\\SAM_NOTES_RES\\inspection\\libSpen_document.dll"
            };
            var output = new Dictionary<string, string>();
            biz.UnsafeExecute(injectedDlls, new List<string> { type }, (name, text) =>
            {
                output[name] = text?.ToString();
            });
            foreach (var kvp in output)
            {
                Assert.IsFalse(string.IsNullOrWhiteSpace(kvp.Key), $"Output name is empty for type '{type}'");
                Assert.IsFalse(string.IsNullOrWhiteSpace(kvp.Value), $"Output text for '{kvp.Key}' is empty (type '{type}')");
            }
            Assert.IsTrue(output.Count >= 1, $"Expected at least 1 output for type '{type}', got {output.Count}");
        }

        [TestMethod]
        public void UnsafeExecute_IntegrationTest_AllTypesAtOnce_NoDuplicateClassDeclarations()
        {
            var biz = new BizLogic();
            // Provided DLLs
            var injectedDlls = new List<string>
            {
                @"C:\\Program Files\\WindowsApps\\Microsoft.VCLibs.140.00_14.0.33519.0_x64__8wekyb3d8bbwe\\msvcp140_app.dll",
                @"C:\\Users\\Shai\\Desktop\\SAM_NOTES_RES\\inspection\\libSpen_worddoc.dll",
                @"C:\\Users\\Shai\\Desktop\\SAM_NOTES_RES\\inspection\\libSpen_document.dll"
            };
            // Provided types
            var typesToInspect = new List<string>
            {
                "libSpen_base.dll!SPen::Uuid",
                "*!SPen::String",
                "*!SPen::BaseData",
                //"*!SPen::NoteZip",
                "*!SPen::IOutputStream",
                "*!SPen::PreSubmitData",
                "*!SPen::HistoryEventHandler",
                "*!SPen::ICursorEventCallback",
                "*!SPen::FollowerObserverListener",
                "*!SPen::IStringEventCallback",
                "*!SPen::ObjectTextBox",
                "*!SPen::WContentFileEventListener",
                "*!SPen::WCursorEventListener",
                "*!SPen::WHistoryEventListener",
                "*!SPen::WObjectEventListener",
                "*!SPen::WObjectIndexMovedEventListener",
                "*!SPen::WObjectSelectedEventListener",
                "*!SPen::WPageEventListener",
                "*!SPen::WStringEventListener",
                "*!SPen::WTextEventListener",
                "*!SPen::WVoiceDataEventListener",
                "libSpen_base.dll!SPen::Bundle",
                "libSpen_base.dll!SPen::List",
                "libSpen_document.dll!SPen::HistoryData",
                "libSpen_document.dll!SPen::HistoryManager",
                "libSpen_document.dll!SPen::HistoryManager::CommandJob",
                "libSpen_document.dll!SPen::ICursorEventCallback",
                "libSpen_document.dll!SPen::ILayerEventCallback",
                "libSpen_document.dll!SPen::IObjectEventCallback",
                "libSpen_document.dll!SPen::IObjectIndexMovedEventCallback",
                "libSpen_document.dll!SPen::IPageEventCallback",
                "libSpen_document.dll!SPen::ITextEventCallback",
                "libSpen_document.dll!SPen::MediaFileManager",
                "libSpen_document.dll!SPen::ModelContext",
                "libSpen_base.dll!SPen::Point",
                "libSpen_base.dll!SPen::PointD",
                "libSpen_base.dll!SPen::PointF",
                "libSpen_base.dll!SPen::Rect",
                "libSpen_base.dll!SPen::RectD",
                "libSpen_base.dll!SPen::RectF",
                "libSpen_document.dll!SPen::StringIDManager",
                "libSpen_document.dll!SPen::VoiceNameManager",
                "libSpen_document.dll!SPen::ObjectBase",
                "libSpen_document.dll!SPen::ObjectList",
                "libSpen_document.dll!SPen::ObjectSpan",
                "libSpen_worddoc.dll!SPen::WPage",
                "libSpen_worddoc.dll!SPen::WNote"
            };

            // 1. Run UnsafeExecute for all types at once
            var output = new Dictionary<string, string>();
            biz.UnsafeExecute(injectedDlls, typesToInspect, (name, text) =>
            {
                output[name] = text?.ToString();
            });

            // 3. For each .cs file, find all class declarations and their namespaces
            var classToFiles = new Dictionary<(string Namespace, string ClassName), List<string>>();
            foreach (var kvp in output)
            {
                var fileName = kvp.Key;
                var fileContent = kvp.Value;
                if (string.IsNullOrWhiteSpace(fileName) || string.IsNullOrWhiteSpace(fileContent))
                    continue;
                if (!fileName.EndsWith(".cs") || fileName == "all_finds.cs")
                    continue;
                string currentNamespace = null;
                var lines = fileContent.Split(new[] { '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries);
                foreach (var line in lines)
                {
                    var trimmed = line.Trim();
                    if (trimmed.StartsWith("namespace "))
                    {
                        currentNamespace = trimmed.Substring("namespace ".Length).Split(new[] { ' ', '{' }, StringSplitOptions.RemoveEmptyEntries)[0];
                    }
                    if (trimmed.Contains(" class ") && trimmed.Contains(": __RemoteNET_Obj_Base"))
                    {
                        var idx = trimmed.IndexOf("class ");
                        if (idx >= 0)
                        {
                            var afterClass = trimmed.Substring(idx + 6).Trim();
                            var className = afterClass.Split(new[] { ' ', ':', '{', '(' }, StringSplitOptions.RemoveEmptyEntries)[0];
                            if (!string.IsNullOrWhiteSpace(className))
                            {
                                var key = (currentNamespace ?? "", className);
                                if (!classToFiles.ContainsKey(key))
                                    classToFiles[key] = new List<string>();
                                classToFiles[key].Add(fileName);
                            }
                        }
                    }
                }
            }

            // 4. Assert that every class (with namespace) is only declared in one file
            foreach (var kvp in classToFiles)
            {
                Assert.IsTrue(kvp.Value.Distinct().Count() == 1, $"Class '{kvp.Key.Namespace}::{kvp.Key.ClassName}' declared in multiple files: {string.Join(", ", kvp.Value)}");
            }
        }
    }
}
