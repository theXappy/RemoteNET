using System;
using System.Runtime.InteropServices;

internal static class AdminCheck
{
    public static bool IsRunningAsAdmin()
    {
        IntPtr tokenHandle = IntPtr.Zero;
        try
        {
            if (!OpenProcessToken(GetCurrentProcess(), TOKEN_QUERY, out tokenHandle))
                return false;

            byte[] elevation = new byte[sizeof(uint)];
            int size = elevation.Length;
            if (!GetTokenInformation(tokenHandle, TOKEN_INFORMATION_CLASS.TokenElevation, elevation, size, out int _))
                return false;

            return BitConverter.ToUInt32(elevation, 0) != 0;
        }
        finally
        {
            if (tokenHandle != IntPtr.Zero)
                CloseHandle(tokenHandle);
        }
    }

    private const int TOKEN_QUERY = 0x0008;

    private enum TOKEN_INFORMATION_CLASS
    {
        TokenElevation = 20
    }

    [DllImport("advapi32.dll", SetLastError = true)]
    private static extern bool OpenProcessToken(IntPtr ProcessHandle, int DesiredAccess, out IntPtr TokenHandle);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr GetCurrentProcess();

    [DllImport("advapi32.dll", SetLastError = true)]
    private static extern bool GetTokenInformation(
        IntPtr TokenHandle,
        TOKEN_INFORMATION_CLASS TokenInformationClass,
        [Out] byte[] TokenInformation,
        int TokenInformationLength,
        out int ReturnLength);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool CloseHandle(IntPtr hObject);
}
