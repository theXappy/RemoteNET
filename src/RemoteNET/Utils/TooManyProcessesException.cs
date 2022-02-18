using System;
using System.Diagnostics;

namespace RemoteNET.Utils
{
    public class TooManyProcessesException : ArgumentException
    {
        public Process[] Matches { get; set; }
        public TooManyProcessesException(string msg, Process[] matches) : base(msg)
        {
            Matches = matches;
        }
    }
}
