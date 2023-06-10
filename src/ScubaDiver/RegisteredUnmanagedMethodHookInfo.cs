using System;
using System.Net;

namespace ScubaDiver
{
    public class RegisteredUnmanagedMethodHookInfo
    {
        /// <summary>
        /// The patch callback that was registered on the method
        /// </summary>
        public Delegate RegisteredProxy { get; set; }

        /// <summary>
        /// The method that was hooked
        /// </summary>
        public UndecoratedFunction OriginalHookedMethod { get; set; }

        /// <summary>
        /// The IP Endpoint listening for invocations
        /// </summary>
        public IPEndPoint Endpoint { get; set; }

    }
}