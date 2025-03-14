using InjectableDotNetHost.Injector;
using RemoteNET.Internal.Extensions;
using RemoteNET.Internal.Utils;
using RemoteNET.Properties;
using System;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;

namespace RemoteNET.Access;

[DebuggerDisplay($"Kit ({nameof(RemoteNetAppDataDir)})")]
public class InjectionToolKit
{
    public string RemoteNetAppDataDir { get; set; }
    public string InjectorPath { get; set; }
    public string UnmanagedAdapterPath { get; set; }
    public string ScubaDiverDllPath { get; set; }
    public string InjectableDummyPath { get; set; }
    public string LifeboatExePath { get; set; }

    private InjectionToolKit(string remoteNetAppDataDir, bool is64Bit, string targetDotNetVer)
    {
        RemoteNetAppDataDir = remoteNetAppDataDir;

        GetNativeTools(is64Bit);
        GetScubaDiver(is64Bit, targetDotNetVer);
        GetLifeboat();

        // Also mark the directories as UWP injectable
        PermissionsHelper.MakeUwpInjectable(RemoteNetAppDataDir);
        PermissionsHelper.MakeUwpInjectable(Path.GetDirectoryName(ScubaDiverDllPath));
        PermissionsHelper.MakeUwpInjectable(Path.GetDirectoryName(InjectableDummyPath));
    }

    private void GetNativeTools(bool is64bit)
    {
        // Decide which injection toolkit to use x32 or x64
        InjectorPath = Path.Combine(RemoteNetAppDataDir, nameof(Resources.Injector) + ".exe");
        UnmanagedAdapterPath = Path.Combine(RemoteNetAppDataDir, nameof(Resources.UnmanagedAdapterDLL) + ".dll");
        string adapterPdbPath = Path.Combine(RemoteNetAppDataDir, nameof(Resources.UnmanagedAdapterDLL) + ".pdb");
        byte[] injectorResource = Resources.Injector;
        byte[] adapterResource = Resources.UnmanagedAdapterDLL;
        byte[] adapterPdbResource = Resources.UnmanagedAdapterDLL_pdb;
        if (is64bit)
        {
            InjectorPath = Path.Combine(RemoteNetAppDataDir, nameof(Resources.Injector_x64) + ".exe");
            UnmanagedAdapterPath = Path.Combine(RemoteNetAppDataDir, nameof(Resources.UnmanagedAdapterDLL_x64) + ".dll");
            adapterPdbPath = Path.Combine(RemoteNetAppDataDir, nameof(Resources.UnmanagedAdapterDLL_x64) + ".pdb");
            injectorResource = Resources.Injector_x64;
            adapterResource = Resources.UnmanagedAdapterDLL_x64;
            adapterPdbResource = Resources.UnmanagedAdapterDLL_x64_pdb;
        }

        // Check if injector or bootstrap resources differ from copies on disk
        OverrideFileIfChanged(InjectorPath, injectorResource);
        OverrideFileIfChanged(UnmanagedAdapterPath, adapterResource);
        OverrideFileIfChanged(adapterPdbPath, adapterPdbResource);
        PermissionsHelper.MakeUwpInjectable(UnmanagedAdapterPath);

        // Check if InjectableDummyPath resources differ from copies on disk
        InjectableDummyPath = Path.Combine(RemoteNetAppDataDir, nameof(Resources.InjectableDummy) + ".dll");
        string injectableDummyPdbPath = Path.Combine(RemoteNetAppDataDir, nameof(Resources.InjectableDummy) + ".pdb");
        string injectableDummyRuntimeConfigPath = Path.Combine(RemoteNetAppDataDir,
            nameof(Resources.InjectableDummy) + ".runtimeconfig.json");
        // Write to disk
        OverrideFileIfChanged(InjectableDummyPath, Resources.InjectableDummy);
        OverrideFileIfChanged(injectableDummyPdbPath, Resources.InjectableDummyPdb);
        OverrideFileIfChanged(injectableDummyRuntimeConfigPath, Resources.InjectableDummy_runtimeconfig);

        // Change permissions
        PermissionsHelper.MakeUwpInjectable(InjectableDummyPath);
        PermissionsHelper.MakeUwpInjectable(injectableDummyPdbPath);
        PermissionsHelper.MakeUwpInjectable(injectableDummyRuntimeConfigPath);
    }

    private static void OverrideFileIfChanged(string path, byte[] data)
    {
        string newDataHash = HashUtils.BufferSHA256(data);
        string existingDataHash = File.Exists(path) ? HashUtils.FileSHA256(path) : String.Empty;
        if (newDataHash != existingDataHash)
        {
            File.WriteAllBytes(path, data);
        }
    }

    private void GetScubaDiver(bool is64Bit, string targetDotNetVer)
    {
        bool isNetCore = targetDotNetVer != "net451";
        bool isNet5 = targetDotNetVer == "net5.0-windows";
        bool isNet6orUp = targetDotNetVer == "net6.0-windows" ||
                          targetDotNetVer == "net7.0-windows" ||
                          targetDotNetVer == "net8.0-windows" ||
                          targetDotNetVer == "net9.0-windows";
        bool isNative = targetDotNetVer == "native";


        // Unzip scuba diver and dependencies into their own directory
        string targetDiver = "ScubaDiver_NetFramework";
        if (isNetCore)
            targetDiver = "ScubaDiver_NetCore";
        if (isNet5)
            targetDiver = "ScubaDiver_Net5";
        if (isNet6orUp || isNative)
            targetDiver = is64Bit ? "ScubaDiver_Net6_x64" : "ScubaDiver_Net6_x86";

        // Dumping Scuba Diver
        var scubaDestDirInfo = new DirectoryInfo(Path.Combine(RemoteNetAppDataDir, targetDiver));
        if (!scubaDestDirInfo.Exists)
        {
            scubaDestDirInfo.Create();
        }
        DumpZip(Resources.ScubaDivers, scubaDestDirInfo, targetDiver);

        // Look for the specific scuba diver according to the target's .NET version
        var matches = scubaDestDirInfo.EnumerateFiles().Where(scubaFile => scubaFile.Name.EndsWith($"{targetDiver}.dll"));
        if (matches.Count() != 1)
        {
            Debugger.Launch();
            throw new Exception($"Expected exactly 1 ScubaDiver dll to match '{targetDiver}' but found: " +
                                matches.Count() + "\n" +
                                "Results: \n" +
                                String.Join("\n", matches.Select(m => m.FullName)) +
                                "Target Framework Parameter: " +
                                targetDotNetVer
            );
        }

        ScubaDiverDllPath = matches.Single().FullName;
    }

    private void GetLifeboat()
    {
        // Dumping Lifeboat
        var lifeboatDestDirInfo = new DirectoryInfo(Path.Combine(RemoteNetAppDataDir, "Lifeboat"));
        if (!lifeboatDestDirInfo.Exists)
        {
            lifeboatDestDirInfo.Create();
        }
        DumpZip(Resources.Lifeboat, lifeboatDestDirInfo);
        LifeboatExePath = Path.Combine(lifeboatDestDirInfo.FullName, "Lifeboat.exe");
    }

    private static void DumpZip(byte[] zip, DirectoryInfo scubaDestDirInfo, string subFolderInZip = null)
    {
        // Temp dir to dump to before moving to app data (where it might have previously deployed files
        // AND they might be in use by some application so they can't be overwritten)
        Random rand = new Random();
        var tempDir = Path.Combine(Path.GetTempPath(), rand.Next(100000).ToString());
        DirectoryInfo tempDirInfo = new DirectoryInfo(tempDir);
        if (tempDirInfo.Exists)
        {
            tempDirInfo.Delete(recursive: true);
        }

        tempDirInfo.Create();
        using (var diverZipMemoryStream = new MemoryStream(zip))
        {
            ZipArchive diverZip = new ZipArchive(diverZipMemoryStream);
            // This extracts the "Scuba" directory from the zip to *tempDir*
            diverZip.ExtractToDirectory(tempDir);
        }

        // Going over unzipped files and checking which of those we need to copy to our AppData directory
        if (subFolderInZip != null)
        {
            tempDirInfo = new DirectoryInfo(Path.Combine(tempDir, subFolderInZip));
        }

        foreach (FileInfo fileInfo in tempDirInfo.GetFiles())
        {
            string destPath = Path.Combine(scubaDestDirInfo.FullName, fileInfo.Name);
            if (File.Exists(destPath))
            {
                string dumpedFileHash = HashUtils.FileSHA256(fileInfo.FullName);
                string previousFileHash = HashUtils.FileSHA256(destPath);
                if (dumpedFileHash == previousFileHash)
                {
                    // Skipping file because the previous version of it has the same hash
                    continue;
                }
            }

            // Moving file to our AppData directory
            File.Delete(destPath);
            fileInfo.MoveTo(destPath);
            // Also set the copy's permissions so we can inject it into UWP apps
            PermissionsHelper.MakeUwpInjectable(destPath);
        }

        // We are done with our temp directory
        tempDirInfo.Delete(recursive: true);
    }
    public static InjectionToolKit GetKit(Process target, string targetDotNetVer)
    {
        // Creating directory to dump the toolkit into: %localappdata%\RemoteNET
        string locaAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        string assemblyName = typeof(ManagedRemoteApp).Assembly.GetName().Name;
        string toolKitDir = Path.Combine(locaAppData, assemblyName);

        var remoteNetAppDataDirInfo = new DirectoryInfo(toolKitDir);
        if (!remoteNetAppDataDirInfo.Exists)
            remoteNetAppDataDirInfo.Create();

        return new InjectionToolKit(toolKitDir, target.Is64Bit(), targetDotNetVer);
    }
}