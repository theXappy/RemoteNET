﻿namespace ScubaDiver.API.Hooking
{
    public class HookContext
    {
        public string StackTrace { get; private set; }
        public bool CallOriginal { get; set; }
        public HookContext(string stackTrace)
        {
            StackTrace = stackTrace;
            CallOriginal = true;
        }
    }
}