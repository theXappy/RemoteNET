using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace RemoteNET.Internal
{
    public static class HashUtils
    {
        public static string FileSHA256(string filePath)
        {
            using (SHA256 SHA256 = SHA256Managed.Create())
            using (FileStream fileStream = File.OpenRead(filePath))
            {
                    return BitConverter.ToString(SHA256.ComputeHash(fileStream)).Replace("-", "").ToLowerInvariant();
            }
        }
        public static string BufferSHA256(byte[] buff)
        {
            using (SHA256 SHA256 = SHA256Managed.Create())
            {
                return BitConverter.ToString(SHA256.ComputeHash(buff)).Replace("-", "").ToLowerInvariant();
            }
        }
    }
}
