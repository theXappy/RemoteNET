using RemoteNET;
using System;
using System.Text;

namespace ConsoleApp
{
    public static class RemoteMarshalExtensions
    {
        public static void QuickDump(this RemoteMarshal m, ulong strValAddress, int length = 0x20)
        {
            byte[] data = m.Read((nint)strValAddress, length);
            Console.WriteLine(GetHexDump(data, strValAddress));
        }

        public static nuint CreateRemoteString(this RemoteMarshal m, string s)
        {
            // Allocate memory for the string, assuming ASCII
            byte[] asciiBytes = Encoding.ASCII.GetBytes(s);
            nint remoteBuf = m.AllocHGlobal(asciiBytes.Length);
            // Copy the string to the remote buffer
            m.Write(asciiBytes, 0, remoteBuf, asciiBytes.Length);
            return (nuint)remoteBuf;
        }

        public static ulong ReadQword(this RemoteMarshal m, ulong address)
        {
            // Read a 64-bit value from the remote process
            byte[] buffer = m.Read((nint)address, 8);
            if (buffer.Length < 8)
                throw new Exception("Failed to read 64-bit value");
            return (nuint)BitConverter.ToUInt64(buffer, 0);
        }

        public static void WriteQword(this RemoteMarshal m, ulong address, ulong value)
        {
            // Convert the 64-bit value to a byte array
            byte[] buffer = BitConverter.GetBytes(value);
            // Write the byte array to the remote process
            m.Write(buffer, 0, (nint)address, buffer.Length);
        }

        public static string GetHexDump(byte[] data, ulong baseAddr)
        {
            // format:
            // 0000000000000000 41 42 43 44 45 46 47 48 49 4a 4b 4c 4d 4e 4f 50 | ABCDEFGHIJKLMNO
            StringBuilder output = new StringBuilder();
            for (int i = 0; i < data.Length; i += 16)
            {
                // Address
                output.Append($"{baseAddr + (ulong)i:X16} ");
                // Hex values
                for (int j = 0; j < 16; j++)
                {
                    if (i + j < data.Length)
                        output.Append($"{data[i + j]:X2} ");
                    else
                        output.Append("   ");
                }
                // ASCII values
                output.Append("| ");
                for (int j = 0; j < 16; j++)
                {
                    if (i + j < data.Length)
                    {
                        // Check if ASCII printable, otherwise print '.'
                        char c = (char)data[i + j];
                        if (char.IsControl(c) || c == 0)
                            output.Append('.');
                        else
                            output.Append(c);
                    }
                    else
                        output.Append(' ');
                }
                output.AppendLine();
            }
            return output.ToString();
        }
    }
}
