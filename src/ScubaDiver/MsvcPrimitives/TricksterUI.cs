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
        return trickster.ScanRegions(typeInfo.XoredVftableAddress, FirstClassTypeInfo.XorMask);
    }

    /// <summary>
    /// Scan the process memory for vftables to spot instances of First-Class types.
    /// </summary>
    /// <param name="trickster"></param>
    /// <param name="typeInfos"></param>
    /// <returns></returns>
    public static Dictionary<FirstClassTypeInfo, IReadOnlyCollection<ulong>> Scan(Trickster trickster, IEnumerable<FirstClassTypeInfo> typeInfos)
    {

        Dictionary< /*xored vftable*/ nuint, FirstClassTypeInfo> xoredVftableToType = new();
        foreach (var typeInfo in typeInfos)
        {
            if (xoredVftableToType.TryGetValue(typeInfo.XoredVftableAddress, out var old))
            {
                Logger.Debug($"[TricksterUI][Scan] Duplicate vftable. Value: 0x{typeInfo.VftableAddress:X16}, Mine: [ {typeInfo} ], Old: [ {old} ]");
                continue;
            }
            xoredVftableToType[typeInfo.XoredVftableAddress] = typeInfo;
        }
        IDictionary</*xored vftable*/ ulong, /*instance pointers*/ IReadOnlyCollection<ulong>> xoredVftablesToInstances = trickster.ScanRegions(xoredVftableToType.Keys, FirstClassTypeInfo.XorMask);

        Dictionary<FirstClassTypeInfo, IReadOnlyCollection<ulong>> res = new();
        foreach (var kvp in xoredVftablesToInstances)
        {
            res[xoredVftableToType[(nuint)kvp.Key]] = kvp.Value;
        }

        return res;
    }
}