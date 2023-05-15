using RemoteNET.Internal.Extensions;
using ScubaDiver.API;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;

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
            DiverCommunicator com = new DiverCommunicator(diverAddr, diverPort);

            bool diverPortIsUse = false;
            try
            {
                IPGlobalProperties ipGlobalProperties = IPGlobalProperties.GetIPGlobalProperties();
                IPEndPoint[] tcpConnInfoArray = ipGlobalProperties.GetActiveTcpListeners();
                List<int> usedPorts = tcpConnInfoArray.Select(conInfo => conInfo.Port).ToList();

                diverPortIsUse = usedPorts.Contains(diverPort);
            }
            catch
            {
                // Had some issues, perhapse it's the diver holding that port.
            }

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
    }
}
