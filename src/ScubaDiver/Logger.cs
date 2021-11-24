using System;
using System.Collections.Generic;
using System.Text;

namespace ScubaDiver
{
    internal class Logger
    {
        internal static void Debug(string s)
        {
#if DEBUG
            Console.WriteLine(s);
#endif
        }
    }
}
