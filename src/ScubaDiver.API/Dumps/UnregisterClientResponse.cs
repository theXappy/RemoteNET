using System;
using System.Collections.Generic;
using System.Text;

namespace ScubaDiver.API.Dumps
{
    public class UnregisterClientResponse
    {
        public bool WasRemvoed { get; set; }
        /// <summary>
        /// Number of remaining clients, after the removal was done
        /// </summary>
        public int OtherClientsAmount { get; set; }
    }
}
