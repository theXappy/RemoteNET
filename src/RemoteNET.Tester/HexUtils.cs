using System.Collections;
using System.Collections.Generic;
using System.Linq;

namespace ScubaDiver.Tester
{
    public static class HexUtils
    {
        public static string ToHex(this object o)
        {
            IEnumerable remoteIenumerable = o as IEnumerable;
            List<byte> combinedBytes = new List<byte>();
            foreach (object o1 in remoteIenumerable)
            {
                byte b = (byte)o1;
                combinedBytes.Add(b);
            }

            return ToHex(combinedBytes);
        }

        private static string ToHex(IEnumerable<byte> bytes)
        {
            return string.Join(separator: "", values: bytes.Select(b => b.ToString("X2")));
        }
    }
}