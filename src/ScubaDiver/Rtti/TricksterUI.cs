﻿using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace TheLeftExit.Trickster.Memory {
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
            return ($"Types found: {Trickster.ScannedTypes.Length}", Trickster.ScannedTypes);
        }

        public string Read() {
            Trickster.ReadRegions();
            return $"Regions read: {Trickster.Regions.Length}";
        }

        public string[] Scan(TypeInfo typeInfo) {
            return Trickster.ScanRegions(typeInfo.Address)
                .Select(x => x.ToString("X"))
                .ToArray();
        }
    }
}
