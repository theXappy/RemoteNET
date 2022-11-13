using System;
using System.Diagnostics;

namespace ScubaDiver
{
    internal class Logger
    {
        public static Lazy<bool> DebugInRelease = new(() => !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("REMOTE_NET_DIVER_MAGIC_DEBUG")));

        internal static void Debug(string s)
        {
#if DEBUG
#else // RELEASE
            // We want to allow debug writing in release only if the magic environment var is set.
            // in debug, this `if` does not exist and logging to console always takes place.
            if(DebugInRelease.Value)
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
