using System.IO;
using System.Security.AccessControl;


namespace RemoteNET.Internal.Utils
{
    public class FilePermissions
    {
        // Adds an ACL entry on the specified file for the specified account.
        public static void AddFileSecurity(string fileName, string account,
            FileSystemRights rights, AccessControlType controlType)
        {

            // Get a FileSecurity object that represents the
            // current security settings.
            FileInfo fInfo = new FileInfo(fileName);
            FileSecurity fSecurity = FileSystemAclExtensions.GetAccessControl(fInfo);

            // Add the FileSystemAccessRule to the security settings.
            fSecurity.AddAccessRule(new FileSystemAccessRule(account,
                rights, controlType));

            // Set the new access settings.
            FileSystemAclExtensions.SetAccessControl(fInfo, fSecurity);
        }

    }
}
