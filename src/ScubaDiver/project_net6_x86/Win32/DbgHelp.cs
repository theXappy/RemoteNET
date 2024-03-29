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

    public static partial class DbgHelp
    {
        /// <inheritdoc cref="UnDecorateSymbolName(win32.Foundation.PCSTR, win32.Foundation.PSTR, uint, uint)"/>
        public static unsafe uint UnDecorateSymbolName(string name, win32.Foundation.PSTR outputString, uint maxStringLength, uint flags)
        {
            fixed (byte* nameLocal = name is object ? global::System.Text.Encoding.UTF8.GetBytes(name) : null)
            {
                uint __result = DbgHelp.UnDecorateSymbolName(new win32.Foundation.PCSTR(nameLocal), outputString, maxStringLength, flags);
                return __result;
            }
        }

        /// <summary>Undecorates the specified decorated C++ symbol name.</summary>
        /// <param name="name">
        /// <para>The decorated C++ symbol name. This name can be identified by the first character of the name, which is always a question mark (?).</para>
        /// <para><see href="https://docs.microsoft.com/windows/win32/api//dbghelp/nf-dbghelp-undecoratesymbolname#parameters">Read more on docs.microsoft.com</see>.</para>
        /// </param>
        /// <param name="outputString">A pointer to a string buffer that receives the undecorated name.</param>
        /// <param name="maxStringLength">The size of the <i>UnDecoratedName</i> buffer, in characters.</param>
        /// <param name="flags">
        /// <para>The options for how the decorated name is undecorated. This parameter can be zero or more of the following values. </para>
        /// <para>This doc was truncated.</para>
        /// <para><see href="https://docs.microsoft.com/windows/win32/api//dbghelp/nf-dbghelp-undecoratesymbolname#parameters">Read more on docs.microsoft.com</see>.</para>
        /// </param>
        /// <returns>
        /// <para>If the function succeeds, the return value is the number of characters in the <i>UnDecoratedName</i> buffer, not including the NULL terminator. If the function fails, the return value is zero. To retrieve extended error information, call <a href="/windows/desktop/api/errhandlingapi/nf-errhandlingapi-getlasterror">GetLastError</a>. If the function fails and returns zero, the content of the <i>UnDecoratedName</i> buffer is undetermined.</para>
        /// </returns>
        /// <remarks>
        /// <para><see href="https://docs.microsoft.com/windows/win32/api//dbghelp/nf-dbghelp-undecoratesymbolname">Learn more about this API from docs.microsoft.com</see>.</para>
        /// </remarks>
        [DllImport("DbgHelp", ExactSpelling = true, SetLastError = true)]
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        public static extern uint UnDecorateSymbolName(win32.Foundation.PCSTR name, win32.Foundation.PSTR outputString, uint maxStringLength, uint flags);
    }
}
