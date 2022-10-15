using System;
using System.Reflection;

namespace ScubaDiver
{
    public class RegisteredMethodHookInfo
    {
        /// <summary>
        /// The patch callback that was registered on the method
        /// </summary>
        public Delegate RegisteredProxy { get; set; }
        /// <summary>
        /// The method that was hooked
        /// </summary>
        public MethodBase OriginalHookedMethod { get; set; }

    }
}