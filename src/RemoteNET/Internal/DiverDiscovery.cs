using RemoteNET.Internal.Extensions;
using ScubaDiver.API;
using System;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Runtime.Versioning;
using static Vanara.PInvoke.IpHlpApi;

namespace RemoteNET.Internal
{
    public enum DiverState
    {
        NoDiver,
        Alive,
        Corpse,
        HollowSnapshot
    }
    public static class DiverDiscovery
    {
        public static void QueryStatus(Process target, out DiverState managedDiverState, out DiverState unmanagedDiverState)
        {
            ushort managedDiverPort = (ushort)target.Id;
            ushort unmanagedDiverPort = (ushort)(target.Id + 2);

            // TODO: Make it configurable
            string diverAddr = "127.0.0.1";
            managedDiverState = QueryStatus(target, diverAddr, managedDiverPort);
            unmanagedDiverState = QueryStatus(target, diverAddr, unmanagedDiverPort);
        }

        public static DiverState QueryStatus(Process target, string diverAddr, int diverPort)
        {
            using DiverCommunicator com = new DiverCommunicator(diverAddr, diverPort, timeout: 500 /* ms */);
            bool diverPortIsUse = CheckPort(target, diverPort);

            if (diverPortIsUse)
            {
                if (com.CheckAliveness())
                {
                    return DiverState.Alive;
                }
            }

            // Diver isn't alive. It's possible that it was never injected or it was injected and killed
            bool containsToolkitDll = false;
            try
            {
                containsToolkitDll |= target.Modules.AsEnumerable()
                                        .Any(module => module.ModuleName.Contains("UnmanagedAdapterDLL"));
            }
            catch
            {
                // Sometimes this happens because x32 vs x64 process interaction is not supported
            }
            if (containsToolkitDll)
            {
                // Check if the this is a snapshot created by the diver.
                if (target.Threads.Count == 0)
                    return DiverState.HollowSnapshot;
                return DiverState.Corpse;
            }
            return DiverState.NoDiver;
        }

        private static bool CheckPort(Process target, int diverPort)
        {
            try
            {
                if (OperatingSystem.IsWindows())
                {
                    return CheckPortWindowsSpecific(target, diverPort);
                }
                else
                {
                    // Fallback for when we support non-windows hosts (yeah, right)
                    // On non-Windows, can we only confirm the suspected port is OPENED by SOME process.
                    IPGlobalProperties ipGlobalProperties = IPGlobalProperties.GetIPGlobalProperties();
                    IPEndPoint[] tcpConnInfoArray = ipGlobalProperties.GetActiveTcpListeners();
                    return tcpConnInfoArray.Any(conInfo => conInfo.Port == diverPort);
                }
            }
            catch
            {
                // Had some issues, perhaps it's the diver holding that port.
                throw;
            }
        }

        [SupportedOSPlatform("windows")]
        private static bool CheckPortWindowsSpecific(Process target, int diverPort)
        {
            // On Windows, can we confirm the suspected port is OPENED by the SPECIFIC TARGET PROCESS.
            MIB_TCPTABLE_OWNER_MODULE openedListenPorts = GetExtendedTcpTable<MIB_TCPTABLE_OWNER_MODULE>(TCP_TABLE_CLASS.TCP_TABLE_OWNER_MODULE_LISTENER);
            ushort swappedPort = (ushort)((diverPort >> 8) | (diverPort << 8));
            var targetOpenedListenPorts = openedListenPorts.Where(row => row.dwLocalPort == diverPort || row.dwLocalPort == swappedPort).ToList();
            if (targetOpenedListenPorts.Any())
            {
                // I only ever expect a single process (or none) to hold the target port opened, but the API here is sh*t
                // and MIB_TCPROW_OWNER_MODULE is not comparable to `default`
                bool diverPortIsUse = targetOpenedListenPorts.Any(t => t.dwOwningPid == target.Id);

                // Fallback - Port might be held in use by the Lifeboat
                if (!diverPortIsUse)
                {
                    var ownerProcs = targetOpenedListenPorts.Select(t => (int)t.dwOwningPid).Select(Process.GetProcessById);
                    diverPortIsUse = ownerProcs.Any(proc => proc.ProcessName == "Lifeboat");
                }
                return diverPortIsUse;
            }

            return false;
        }
    }
}
