﻿namespace ScubaDiver.API.Interactions.Dumps
{
    /// <summary>
    /// Dump of a specific member (field, property) of a specific object
    /// </summary>
    public class MemberDump
    {
        public string Name { get; set; }
        public bool HasEncodedValue { get; set; }
        public string EncodedValue { get; set; }
        public string RetrivalError { get; set; }
    }
}