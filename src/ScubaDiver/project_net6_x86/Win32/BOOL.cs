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
        public readonly partial struct BOOL
        {
            private readonly int value;

            public int Value => this.value;
            public BOOL(bool value) => this.value = Unsafe.As<bool, sbyte>(ref value);
            public BOOL(int value) => this.value = value;
            public static implicit operator bool(BOOL value)
            {
                sbyte v = checked((sbyte)value.value);
                return Unsafe.As<sbyte, bool>(ref v);
            }
            public static implicit operator BOOL(bool value) => new BOOL(value);
            public static explicit operator BOOL(int value) => new BOOL(value);
        }
    }
}