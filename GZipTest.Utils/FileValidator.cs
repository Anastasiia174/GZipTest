using System;
using System.IO;
using System.Security.AccessControl;

namespace GZipTest.Utils
{
    public static class FileValidator
    {
        public static bool ValidateFileExists(string filePath)
        {
            return File.Exists(filePath);
        }

        public static bool ValidateFileIsReadOnly(string filePath)
        {
            var info = new FileInfo(filePath);
            return info.Attributes.HasFlag(FileAttributes.ReadOnly);
        }

        public static bool ValidateFileIsDirectory(string filePath)
        {
            var info = new FileInfo(filePath);
            return info.Attributes.HasFlag(FileAttributes.Directory);
        }

        public static bool ValidateFileExtension(string filePath, string extension)
        {
            var info = new FileInfo(filePath);
            return info.Extension == extension;
        }

        public static bool ValidateWritePermissionToDirectory(string directoryPath)
        {
            var writeAllow = false;
            var writeDeny = false;
            var accessControlList = Directory.GetAccessControl(directoryPath);
            if (accessControlList == null)
                return false;
            var accessRules = accessControlList.GetAccessRules(true, true,
                typeof(System.Security.Principal.SecurityIdentifier));

            foreach (FileSystemAccessRule rule in accessRules)
            {
                if ((FileSystemRights.Write & rule.FileSystemRights) != FileSystemRights.Write)
                    continue;

                if (rule.AccessControlType == AccessControlType.Allow)
                    writeAllow = true;
                else if (rule.AccessControlType == AccessControlType.Deny)
                    writeDeny = true;
            }

            return writeAllow && !writeDeny;
        }
    }
}
