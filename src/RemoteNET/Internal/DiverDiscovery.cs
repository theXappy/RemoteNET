using RemoteNET.Internal.Extensions;
using ScubaDiver.API;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;

namespace RemoteNET.Internal
{
    public enum DiverState
    {
        NoDiver,
        Alive,
        Corpse
    }
    public static class DiverDiscovery
    {
        public static DiverState QueryStatus(Process target)
        {
            ushort diverPort = (ushort)target.Id;

            // TODO: Make it configurable
            string diverAddr = "127.0.0.1";
            DiverCommunicator com = new DiverCommunicator(diverAddr, diverPort);

            bool isAlive = com.CheckAliveness();
            
            if(isAlive)
            {
                return DiverState.Alive;
            }

            // Diver isn't alive. It's possible that it was never injected or it was injected and killed
            bool adapterModuleAlreadyInjected = false;
            try
            {
                adapterModuleAlreadyInjected = target.Modules.AsEnumerable()
                                        .Any(module => module.ModuleName.Contains("UnmanagedAdapterDLL"));
            }
            catch
            {
                // Sometimes this happens because x32 vs x64 process interaction is not supported
            }
            if (adapterModuleAlreadyInjected)
            {
                return DiverState.Corpse;
            }
            return DiverState.NoDiver;
        }
    }
}
