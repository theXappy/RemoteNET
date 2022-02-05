using System;
using System.Reflection;

namespace ScubaDiver
{
    public class RegisteredEventHandlerInfo 
    {
        /// <summary>
        /// Event handler that was registered on the event
        /// </summary>
        public Delegate RegisteredProxy { get; set; }
        // Note that this object might be pinned or unpinned when this info object is created
        // but by holding a reference to it within this class we don't care if it moves or
        // not - we will always be able to safely access it
        public object Target { get; set; }
        public EventInfo EventInfo { get; set; }
    }
}