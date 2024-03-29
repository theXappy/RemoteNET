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
        /// <summary>
        /// A pointer to a constant character string.
        /// </summary>
        [DebuggerDisplay("{" + nameof(DebuggerDisplay) + "}")]
        public unsafe readonly partial struct PCSTR
            : IEquatable<PCSTR>
        {
            public readonly byte* Value;
            public PCSTR(byte* value) => this.Value = value;
            public static implicit operator byte*(PCSTR value) => value.Value;
            public static explicit operator PCSTR(byte* value) => new PCSTR(value);
            public static implicit operator PCSTR(PSTR value) => new PCSTR(value.Value);

            public bool Equals(PCSTR other) => this.Value == other.Value;

            public override bool Equals(object obj) => obj is PCSTR other && this.Equals(other);

            public override int GetHashCode() => unchecked((int)this.Value);

            public int Length
            {
                get
                {
                    byte* p = this.Value;
                    if (p is null)
                        return 0;
                    while (*p != 0)
                        p++;
                    return checked((int)(p - this.Value));
                }
            }


            /// <summary>
            /// Returns a <see langword="string"/> with a copy of this character array, decoding as UTF-8.
            /// </summary>
            /// <returns>A <see langword="string"/>, or <see langword="null"/> if <see cref="Value"/> is <see langword="null"/>.</returns>
            public override string ToString() => this.Value is null ? null : new string((sbyte*)this.Value, 0, this.Length, global::System.Text.Encoding.UTF8);

            public ReadOnlySpan<byte> AsSpan() => this.Value is null ? default(ReadOnlySpan<byte>) : new ReadOnlySpan<byte>(this.Value, this.Length);


            private string DebuggerDisplay => this.ToString();
        }
    }
}
