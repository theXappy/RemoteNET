﻿namespace ScubaDiver.API.Interactions.Client
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
