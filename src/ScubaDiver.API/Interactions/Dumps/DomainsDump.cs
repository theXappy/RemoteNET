﻿using System.Collections.Generic;

namespace ScubaDiver.API.Interactions.Dumps
{
    public class DomainsDump
    {
        public class AvailableDomain
        {
            public string Name { get; set; }
            public List<string> AvailableModules { get; set; }
        }
        public string Current { get; set; }
        public List<AvailableDomain> AvailableDomains { get; set; }
    }
}