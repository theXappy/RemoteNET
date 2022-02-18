using System;
using System.IO;
using System.Security.Cryptography;

namespace RemoteNET.Internal.Extensions
{
    public static class HashUtils
    {
        public static string FileSHA256(string filePath)
        {
            using (SHA256 SHA256 = System.Security.Cryptography.SHA256.Create())
            using (FileStream fileStream = File.OpenRead(filePath))
            {
                return BitConverter.ToString(SHA256.ComputeHash(fileStream)).Replace("-", "").ToLowerInvariant();
            }
        }
        public static string BufferSHA256(byte[] buff)
        {
            using (SHA256 SHA256 = System.Security.Cryptography.SHA256.Create())
            {
                return BitConverter.ToString(SHA256.ComputeHash(buff)).Replace("-", "").ToLowerInvariant();
            }
        }
    }
}
