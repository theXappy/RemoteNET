﻿using System.Collections.Generic;

namespace RemoteNET
{
    public enum RuntimeType
    {
        Unknown = 0,
        Managed,
        Unmanaged
    }

    public class CandidateType
    {
        public RuntimeType Runtime { get; set; }
        public string TypeFullName { get; set; }
        public string Assembly { get; set; }
        public ulong? MethodTable { get; set; }

        public CandidateType(RuntimeType runtime, string typeName, string assembly, ulong? methodTable)
        {
            Runtime = runtime;
            TypeFullName = typeName;
            Assembly = assembly;
            MethodTable = methodTable;
        }

        public override string ToString() => $"[{Runtime}] {Assembly}!{TypeFullName}";
    }
}
