using System.Net;
using ScubaDiver.Hooking;

namespace ScubaDiver
{
    public class RegisteredManagedMethodHookInfo
    {
        /// <summary>
        /// Hook callback that was registered with HookingCenter
        /// </summary>
        public HarmonyWrapper.HookCallback RegisteredProxy { get; set; }

        /// <summary>
        /// Endpoint listening for invocations of the hook
        /// </summary>
        public IPEndPoint Endpoint { get; set; }

        /// <summary>
        /// Unique identifier for this method hook (method + position)
        /// Used to coordinate with HookingCenter for unhooking
        /// </summary>
        public string UniqueHookId { get; set; }
    }
}