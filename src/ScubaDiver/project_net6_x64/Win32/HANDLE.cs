﻿// ------------------------------------------------------------------------------
// <auto-generated>
//     This code was generated by a tool.
//
//     Changes to this file may cause incorrect behavior and will be lost if
//     the code is regenerated.
// </auto-generated>
// ------------------------------------------------------------------------------

#pragma warning disable CS1591,CS1573,CS0465,CS0649,CS8019,CS1570,CS1584,CS1658,CS0436
namespace Windows.Win32
{
    using global::System;
    using global::System.Diagnostics;
    using global::System.Runtime.CompilerServices;
    using global::System.Runtime.InteropServices;
    using global::System.Runtime.Versioning;
    using win32 = global::Windows.Win32;

    namespace Foundation
    {
        [DebuggerDisplay("{Value}")]
        public readonly partial struct HANDLE
            : IEquatable<HANDLE>
        {
            public readonly IntPtr Value;
            public HANDLE(IntPtr value) => this.Value = value;

            public bool IsNull => Value == default;
            public static implicit operator IntPtr(HANDLE value) => value.Value;
            public static explicit operator HANDLE(IntPtr value) => new HANDLE(value);
            public static bool operator ==(HANDLE left, HANDLE right) => left.Value == right.Value;
            public static bool operator !=(HANDLE left, HANDLE right) => !(left == right);

            public bool Equals(HANDLE other) => this.Value == other.Value;

            public override bool Equals(object obj) => obj is HANDLE other && this.Equals(other);

            public override int GetHashCode() => this.Value.GetHashCode();
        }
    }
}