using System;
using System.IO;
using System.Security.AccessControl;

namespace GZipTest.Utils
{
    /// <summary>
    /// Util class for validating files
    /// </summary>
    public static class FileValidator
    {
        /// <summary>
        /// Validates if file exists
        /// </summary>
        /// <param name="filePath">Path to file for validation</param>
        /// <returns>True if file exists, false otherwise</returns>
        public static bool ValidateFileExists(string filePath)
        {
            return File.Exists(filePath);
        }

        /// <summary>
        /// Validates if file is read-only
        /// </summary>
        /// <param name="filePath">Path to file for validation</param>
        /// <returns>True if file is read-only, false otherwise</returns>
        public static bool ValidateFileIsReadOnly(string filePath)
        {
            var info = new FileInfo(filePath);
            return info.Attributes.HasFlag(FileAttributes.ReadOnly);
        }

        /// <summary>
        /// Validates if file has specified extension
        /// </summary>
        /// <param name="filePath">Path to file for validation</param>
        /// <param name="extension">File extension</param>
        /// <returns>True if has specified extension, false otherwise</returns>
        public static bool ValidateFileExtension(string filePath, string extension)
        {
            var info = new FileInfo(filePath);
            return info.Extension == extension;
        }

        /// <summary>
        /// Validates if current user has write permission to specified directory
        /// </summary>
        /// <param name="directoryPath"></param>
        /// <returns>True if has write permission, false otherwise</returns>
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
