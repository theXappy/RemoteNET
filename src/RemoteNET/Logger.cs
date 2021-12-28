using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;

namespace RemoteNET
{
    internal class Logger
    {
        internal static void Debug(string s)
        {
#if DEBUG
            Console.WriteLine(s);
#endif
            if(Debugger.IsAttached)
            {
                System.Diagnostics.Debug.WriteLine(s);
            }
        }
    }
}
