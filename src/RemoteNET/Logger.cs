using System;
using System.Diagnostics;

namespace RemoteNET
{
    internal class Logger
    {
        internal static Lazy<bool> _debugInRelease = new Lazy<bool>(() => !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("REMOTE_NET_CLIENT_MAGIC_DEBUG")));

        internal static void Debug(string s)
        {
#if DEBUG
#else // RELEASE
            // We want to allow debug writing in release only if the magic enviroment var is set.
            // in debug, this `if` does not exist and logging to console always takes place.
            if(_debugInRelease.Value)
#endif
            {
                Console.WriteLine(s);
            }

            if(Debugger.IsAttached)
            {
                System.Diagnostics.Debug.WriteLine(s);
            }
        }
    }
}
