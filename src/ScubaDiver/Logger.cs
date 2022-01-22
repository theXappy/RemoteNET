using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;

namespace ScubaDiver
{
    internal class Logger
    {
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
