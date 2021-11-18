using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;

namespace ScubaDiver.Utils
{
    /// <summary>
    /// This class is used to make arbitrary objects "Pinnable" in the .NET process's heap.
    /// Other objects are casted to it using "Unsafe.As" and then their first field's
    /// address overlaps with this class's only field - <see cref="Data"/>.
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    internal sealed class Pinnable
    {
        public byte Data;
    }
}
