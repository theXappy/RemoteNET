using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Security.Principal;
using System.IO;
using System.Linq;
using System.Text;

public class BizLogic
{
    // Centralized hardcoded paths as const fields
    private const string LogFilePath = @"C:\Users\Shai\AppData\Local\Temp\logz\a.txt";
    private const string DumpExePath = @"C:\git\rnet-kit\rnet-class-dump\bin\Debug\net8.0\rnet-class-dump.exe";
    private const string VesselDllPath = @"C:\git\rnet-kit\rnet-kit\bin\Debug\net8.0\RemoteNET.Vessel.dll";
    private const string InjectExePath = @"C:\git\rnet-kit\rnet-inject\bin\Debug\net8.0\rnet-inject.exe";

    private static string VesselWorkingDir => Path.GetDirectoryName(VesselDllPath);
    private static string InjectWorkingDir => Path.GetDirectoryName(InjectExePath);

    public static void Log(string l)
    {
        // Allow concurrent writes from multiple processes using FileShare.Write and retry on IOException
        const int maxRetries = 10;
        const int delayMs = 20;
        for (int i = 0; i < maxRetries; i++)
        {
            try
            {
                string dir = Path.GetDirectoryName(LogFilePath);
                if (!Directory.Exists(dir))
                    Directory.CreateDirectory(dir);

                using (var fs = new FileStream(LogFilePath, FileMode.Append, FileAccess.Write, FileShare.ReadWrite))
                using (var sw = new StreamWriter(fs))
                {
                    sw.Write(l);
                }
                break;
            }
            catch (IOException)
            {
                Debugger.Launch();
                if (i == maxRetries - 1) throw;
                System.Threading.Thread.Sleep(delayMs);
            }
        }
    }

    // Wrapper: old signature, does file access
    public void UnsafeExecuteFromFilePaths(List<string> additionalFiles, Action<string, SourceText> addSourceFile, Action<string> reportError)
    {
        Log("============ Listing Additional Files\n");
        foreach (var additionalFile in additionalFiles)
        {
            Log($"Additional File: {additionalFile}\n");
        }
        var (inspectedDllsFilePath, inspectedTypesFilePath) = GetInspectedFilePaths(additionalFiles);
        if (inspectedDllsFilePath == null || inspectedTypesFilePath == null)
        {
            Log("InspectedDlls.txt or InspectedTypes.txt not found\n");
            return;
        }
        var (inspectedDllsContent, inspectedTypesContent) = ReadInputFiles(inspectedDllsFilePath, inspectedTypesFilePath);
        var inspectedDlls = inspectedDllsContent.Split(new[] { '\n', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries).ToList();
        var inspectedTypes = inspectedTypesContent.Split(new[] { '\n', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries).ToList();
        UnsafeExecute(inspectedDlls, inspectedTypes, addSourceFile, reportError);
    }

    // New method: main logic, takes contents
    public void UnsafeExecute(
        List<string> inspectedDlls,
        List<string> inspectedTypes,
        Action<string, SourceText> addSourceFile,
        Action<string> reportError = null
    )
    {
        // Check for admin privileges
        Log("[Admin Privileges Check] Started\n");
        if (!AdminCheck.IsRunningAsAdmin())
        {
            string msg = "RemoteNET Source Generator requires administrator privileges. Please restart Visual Studio as Administrator.";
            Log($"[Admin Privileges Check] Failed! {msg}");
            reportError?.Invoke(msg);
            return;
        }
        Log("[Admin Privileges Check]] Success.\n");

        string dumpExeVersion = FileVersionInfo.GetVersionInfo(DumpExePath).FileVersion ?? "unknown";
        string stdout = null;

        string inspectedTypesContent = string.Join("\n", inspectedTypes);
        string currentKey = CreateCacheKey();
        var (cacheFolder, keyFilePath, stdoutCachePath) = GetCachePaths();
        bool cacheHit = IsCacheValid(keyFilePath, stdoutCachePath, currentKey);
        if (cacheHit)
        {
            Log("[!!!] Cache hit. Loading stdout from cache.\n");
            stdout = File.ReadAllText(stdoutCachePath);
        }
        else
        {
            Log("[!!!] Cache MISS :( key mismatch or no cache yet.....\n");
            Log(">> Starting to spawn and analyze...\n");
            // Write temp files for compatibility with SpawnAndAnalyze
            using (TempFile typesTempFile = new TempFile(inspectedTypesContent))
            {
                stdout = SpawnAndAnalyze(inspectedDlls, typesTempFile.FilePath);
            }

            if (string.IsNullOrEmpty(stdout))
                return;
            WriteCache(keyFilePath, stdoutCachePath, currentKey, stdout);
        }
        Dictionary<string, string> generatedFiles = ParseGeneratedFiles(stdout);
        AddGeneratedSources(generatedFiles, addSourceFile);
        Log(">> Done writing files\n");
        return;

        string CreateCacheKey()
        {
            string inspectedDllsContent = string.Join("\n", inspectedDlls);
            Log($"InspectedDlls: {inspectedDllsContent}\n");
            Log($"InspectedTypes: {inspectedTypesContent}\n");
            return $"{inspectedDllsContent}{inspectedTypesContent}{dumpExeVersion}";
        }
    }

    public (string, string) GetInspectedFilePaths(List<string> additionalFiles)
    {
        string inspectedDllsFilePath = additionalFiles.FirstOrDefault(x => x.EndsWith("InspectedDlls.txt"));
        string inspectedTypesFilePath = additionalFiles.FirstOrDefault(x => x.EndsWith("InspectedTypes.txt"));
        return (inspectedDllsFilePath, inspectedTypesFilePath);
    }

    public (string, string, string) GetCachePaths()
    {
        string appDataFolder = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        string cacheFolder = Path.Combine(appDataFolder, "RemoteNetSourceGenCache");
        string keyFilePath = Path.Combine(cacheFolder, "key.txt");
        string stdoutCachePath = Path.Combine(cacheFolder, "stdout.txt");
        Directory.CreateDirectory(cacheFolder);
        return (cacheFolder, keyFilePath, stdoutCachePath);
    }

    public (string, string) ReadInputFiles(string inspectedDllsFilePath, string inspectedTypesFilePath)
    {
        string inspectedDllsContent = File.ReadAllText(inspectedDllsFilePath);
        string inspectedTypesContent = File.ReadAllText(inspectedTypesFilePath);
        return (inspectedDllsContent, inspectedTypesContent);
    }

    public bool IsCacheValid(string keyFilePath, string stdoutCachePath, string currentKey)
    {
        if (File.Exists(keyFilePath) && File.Exists(stdoutCachePath))
        {
            string cachedKey = File.ReadAllText(keyFilePath);
            return cachedKey == currentKey;
        }
        return false;
    }

    public void WriteCache(string keyFilePath, string stdoutCachePath, string key, string stdout)
    {
        File.WriteAllText(keyFilePath, key);
        File.WriteAllText(stdoutCachePath, stdout);
    }

    // Refactored to return <classFullName, filePath> mappings
    public Dictionary<string, string> ParseGeneratedFiles(string stdout)
    {
        var dict = new Dictionary<string, string>();
        var lines = stdout.Split(new[] { Environment.NewLine }, StringSplitOptions.RemoveEmptyEntries);
        foreach (var line in lines)
        {
            var parts = line.Split('|');
            if (parts.Length >= 2)
            {
                var key = parts[0].Trim();
                var value = parts[1].Trim();
                dict[key] = value;
            }
        }
        return dict;
    }

    public void AddGeneratedSources(Dictionary<string, string> generatedFiles, Action<string, SourceText> addSourceFile)
    {
        Log(">> Starting to write files\n");
        Log(">> Adding Source all_finds.cs\n");
        addSourceFile($"all_finds.cs", SourceText.From($"// {generatedFiles.Count} num of lines", Encoding.UTF8));
        Log(">> Adding Source all_finds.cs -- added\n");
        foreach (var generatedFile in generatedFiles)
        {
            Log($">> Trying to add next line. Key: {generatedFile.Key}, Value: {generatedFile.Value}\n");
            string classFullNameNormalized = generatedFile.Key.Replace('!', '_');
            string filePath = generatedFile.Value;
            Log($">> Writing: {filePath}\n");
            addSourceFile($"{classFullNameNormalized}.cs", SourceText.From(File.ReadAllText(filePath), Encoding.UTF8));
        }
    }

    public string SpawnAndAnalyze(List<string> targetDlls, string inspectedTypesFilePath)
    {
        var victimProc = StartVictimProcess();
        if (victimProc == null)
        {
            Log("Failed to start Vessel.exe\n");
            return null;
        }
        if (targetDlls == null)
            return null;
        foreach (string dllPath in targetDlls)
        {
            if (!InjectDll(victimProc, dllPath))
                return null;
        }
        Log(">> All DLLs injected successfully\n");
        string stdout = RunClassDump(victimProc, inspectedTypesFilePath);
        TryKillProcess(victimProc);
        return stdout;
    }

    public Process StartVictimProcess()
    {
        Log("Starting `dotnet Vessel.exe`...\n");
        var victimStartInfo = new ProcessStartInfo
        {
            FileName = "dotnet",
            Arguments = VesselDllPath,
            WorkingDirectory = VesselWorkingDir,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };
        var proc = Process.Start(victimStartInfo);
        Log($"Started `dotnet Vessel.exe`. PID: {proc.Id}\n");

        // Start background tasks to read STDOUT and STDERR and log them
        Log("Starting background tasks to read Vessel STDOUT and STDERR...\n");
        Log("Starting background tasks to read Vessel STDOUT and STDERR...\n");
        Log("Starting background tasks to read Vessel STDOUT and STDERR...\n");
        System.Threading.Tasks.Task.Run(() =>
        {
            try
            {
                string line;
                while ((line = proc.StandardOutput.ReadLine()) != null)
                {
                    Log($"[Vessel STDOUT] {line}\n");
                }
            }
            catch (Exception ex)
            {
                Log($"[Vessel STDOUT] Exception: {ex.Message}\n");
            }
        });
        System.Threading.Tasks.Task.Run(() =>
        {
            try
            {
                string line;
                while ((line = proc.StandardError.ReadLine()) != null)
                {
                    Log($"[Vessel STDERR] {line}\n");
                }
            }
            catch (Exception ex)
            {
                Log($"[Vessel STDERR] Exception: {ex.Message}\n");
            }
        });

        return proc;
    }

    public List<string> ReadTargetDlls(string inspectedDllsFilePath, Process victimProc)
    {
        try
        {
            return File.ReadAllLines(inspectedDllsFilePath).Select(x => x.Trim()).ToList();
        }
        catch (Exception e)
        {
            Log($"Error reading target list: {e.Message}\n");
            Debug.WriteLine("Error reading target list: " + e.Message);
            TryKillProcess(victimProc);
            return null;
        }
    }

    public bool InjectDll(Process victimProc, string dllPath)
    {
        Log($"GENERATOR Injecting: {dllPath}\n");
        var injectStartInfo = new ProcessStartInfo
        {
            FileName = InjectExePath,
            WorkingDirectory = InjectWorkingDir,
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
            TryKillProcess(victimProc);
            return false;
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
            TryKillProcess(victimProc);
            return false;
        }
        return true;
    }

    public string RunClassDump(Process victimProc, string inspectedTypesFilePath)
    {
        var dumpStartInfo = new ProcessStartInfo
        {
            FileName = DumpExePath,
            Arguments = $"-v -u -t {victimProc.Id} -l \"{inspectedTypesFilePath}\"",
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
            TryKillProcess(victimProc);
            return null;
        }
        StringBuilder stdoutBuilder = new StringBuilder();
        StringBuilder stderrBuilder = new StringBuilder();
        System.Threading.Thread stdoutThread = new System.Threading.Thread(() => {
            string line;
            while ((line = dumpProc.StandardOutput.ReadLine()) != null)
            {
                stdoutBuilder.AppendLine(line);
            }
        });
        System.Threading.Thread stderrThread = new System.Threading.Thread(() => {
            string line;
            while ((line = dumpProc.StandardError.ReadLine()) != null)
            {
                stderrBuilder.AppendLine(line);
            }
        });
        Log("Starting threads to read STDOUT and STDERR...\n");
        stdoutThread.Start();
        stderrThread.Start();
        Log("Threads started\n");
        Log("Waiting for rnet-class-dump.exe to finish...\n");
        dumpProc.WaitForExit();
        Log("rnet-class-dump.exe finished\n");
        stdoutThread.Join();
        stderrThread.Join();
        Log("Threads joined\n");
        string dumpStdErr = stderrBuilder.ToString();
        string stdout = stdoutBuilder.ToString();
        Log($"rnet-class-dump STDERR: {dumpStdErr}\n");
        Log($"rnet-class-dump STDOUT: {stdout}\n");
        return stdout;
    }

    public void TryKillProcess(Process proc)
    {
        try
        {
            if (!proc.HasExited)
            {
                proc.Kill();
                Log($"Process {proc.Id} killed successfully.\n");
            }
        }
        catch (Exception ex)
        {
            Log($"Failed to kill process {proc.Id}: {ex.Message}\n");
        }
    }
}

public class TempFile : IDisposable
{
    public string FilePath { get; private set; }

    public TempFile(string content)
    {
        FilePath = Path.GetTempFileName();
        File.WriteAllText(FilePath, content);
    }

    public void Dispose()
    {
        if (File.Exists(FilePath))
        {
            try
            {
                File.Delete(FilePath);
            }
            catch (IOException ex)
            {
                // Log or handle the exception as needed
                Console.WriteLine($"Failed to delete temp file {FilePath}: {ex.Message}");
            }
        }
    }
}
