using System;
using System.Diagnostics;

namespace ScubaDiver
{
    internal class Logger
    {
        internal static Lazy<bool> _debugInRelease = new(() => !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("REMOTE_NET_MAGIC_DEBUG")));

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

            if (Debugger.IsAttached)
            {
                System.Diagnostics.Debug.WriteLine(s);
            }

        }
    }
}
