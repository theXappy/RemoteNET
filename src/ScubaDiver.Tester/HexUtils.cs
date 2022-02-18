using System.Collections.Generic;
using System.Linq;

namespace ScubaDiver.Tester
{
    public static class HexUtils
    {
        public static string ToHex(this IEnumerable<byte> bytes)
        {
            return string.Join(separator: "", values: bytes.Select(b => b.ToString("X2")));
        }
    }
}