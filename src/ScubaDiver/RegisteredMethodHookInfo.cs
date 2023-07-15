using System;
using System.Net;
using System.Reflection;

namespace ScubaDiver
{
    public class RegisteredManagedMethodHookInfo
    {
        /// <summary>
        /// The patch callback that was registered on the method
        /// </summary>
        public Delegate RegisteredProxy { get; set; }

        /// <summary>
        /// The IP Endpoint listening for invocations
        /// </summary>
        public IPEndPoint Endpoint { get; set; }

        /// <summary>
        /// The method that was hooked
        /// </summary>
        public Action UnhookAction{ get; set; }

    }
}