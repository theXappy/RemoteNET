using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;

namespace ScubaDiver.Rtti;

public class TricksterUI {
    public Trickster Trickster;
    public Process Process;

    public string OpenProcess(string inputText = null) {
        if (Trickster is not null || string.IsNullOrWhiteSpace(inputText)) {
            Trickster?.Dispose();
            Trickster = null;
            return "Type in the process ID or part of its name.";
        }

        if (int.TryParse(inputText, out int processId)) {
            try {
                Process = Process.GetProcessById(processId);
                Trickster = new(Process);
                return $"Process: {Process.ProcessName} [{Process.Id}]";
            } catch (ArgumentException) {
                // No process with specified ID found; processing input as part of the name.
            }
        }

        Process[] result = Process.GetProcesses()
            .Where(x => x.ProcessName.Contains(inputText, StringComparison.OrdinalIgnoreCase))
            .ToArray();

        switch (result.Length) {
            case 0:
                return "No processes found matching this ID or name.";
            case 1:
                Process = result.Single();
                Trickster = new(Process);
                return $"Process: {Process.ProcessName} [{Process.Id}]";
            default:
                return "More than one process found matching this name.";
        }
    }

    public (string, TypeInfo[]) GetTypes() {
        Trickster.ScanTypes();
        var allTypes = Trickster.ScannedTypes.SelectMany(x => x.Value).ToArray();
        return ($"Types found: {allTypes.Length}", allTypes);
    }

    public string Read() {
        Trickster.ReadRegions();
        return $"Regions read: {Trickster.Regions.Length}";
    }

    public static ulong[] Scan(Trickster trickster, FirstClassTypeInfo typeInfo) {
        return trickster.ScanRegions(typeInfo.Address);
    }

    /// <summary>
    /// Scan the process memory for vftables to spot instances of First-Class types.
    /// </summary>
    /// <param name="trickster"></param>
    /// <param name="typeInfo"></param>
    /// <returns></returns>
    public static Dictionary<FirstClassTypeInfo, IReadOnlyCollection<ulong>> Scan(Trickster trickster, IEnumerable<FirstClassTypeInfo> typeInfo)
    {
        Dictionary<nuint, FirstClassTypeInfo> mtToType = typeInfo.ToDictionary(ti => ti.Address);
        IDictionary<ulong, IReadOnlyCollection<ulong>> matches = trickster.ScanRegions(mtToType.Keys);

        Dictionary<FirstClassTypeInfo, IReadOnlyCollection<ulong>> res = new();
        foreach (var kvp in matches)
        {
            res[mtToType[(nuint)kvp.Key]] = kvp.Value;
        }

        return res;
    }

    public string[] Scan(FirstClassTypeInfo typeInfo) 
        => Scan(Trickster, typeInfo).Select(x => x.ToString("X")).ToArray();
}