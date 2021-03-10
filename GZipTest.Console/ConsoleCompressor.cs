using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.AccessControl;
using CommandLine;

namespace GZipTest
{
    class ConsoleCompressor
    {
        private const string Title = "GZip test tool for compressing files";

        static void Main(string[] args)
        {
            Console.Title = Title;

            IFileCompressor fileCompressor = new MultiThreadFileCompressor();

            CommandLine.Parser.Default.ParseArguments<CompressOptions, DecompressOptions>(args)
                .MapResult(
                    (CompressOptions opts) => RunCompressAndReturnExitCode(opts, fileCompressor),
                    (DecompressOptions opts) => RunDecompressAndReturnExitCode(opts, fileCompressor),
                    errs => 1
                );
        }

        /// <summary>
        /// Runs decompression process
        /// </summary>
        /// <param name="opts">Decompression options</param>
        /// <param name="compressor">File compressor used for processing</param>
        /// <returns>0 if success, 1 otherwise</returns>
        private static int RunDecompressAndReturnExitCode(DecompressOptions opts, IFileCompressor compressor)
        {
            var errors = new List<ArgumentError>();
            CheckFileAccess(opts.SourceFilePath, errors, false);
            CheckFileAccess(opts.OutputFilePath, errors, true);

            if (errors.Any())
            {
                errors.ForEach(err => Console.WriteLine(err.Message));

                if (errors.Any(err => !err.IsWarning))
                {
                    return 1;
                }
            }

            compressor.Option = CompressorOption.Decompress;
            compressor.ProcessFile(opts.SourceFilePath, opts.OutputFilePath);

            return 0;
        }

        /// <summary>
        /// Runs compression process
        /// </summary>
        /// <param name="opts">Compression options</param>
        /// <param name="compressor">File compressor used for processing</param>
        /// <returns>0 if success, 1 otherwise</returns>
        private static int RunCompressAndReturnExitCode(CompressOptions opts, IFileCompressor compressor)
        {
            var errors = new List<ArgumentError>();
            CheckFileAccess(opts.SourceFilePath, errors, false);
            CheckFileAccess(opts.OutputFilePath, errors, true);

            if (errors.Any())
            {
                errors.ForEach(err => Console.WriteLine(err.Message));

                if (errors.Any(err => !err.IsWarning))
                {
                    return 1;
                }
            }

            compressor.ProcessFile(opts.SourceFilePath, opts.OutputFilePath);

            return 0;
        }

        /// <summary>
        /// Checks file access permissions
        /// </summary>
        /// <param name="filePath">The path to file</param>
        /// <param name="errors">The list of errors</param>
        /// <param name="isWriting">Indicates if file would be used for writing</param>
        private static void CheckFileAccess(string filePath, List<ArgumentError> errors, bool isWriting)
        {
            errors ??= new List<ArgumentError>();

            string errorMessageFormat = "Error: {0}";
            string warningMessageFormat = "Warning: {0}";

            var info = new FileInfo(filePath);
            if (isWriting)
            {
                if (!info.Directory.Exists)
                {
                    errors.Add(new ArgumentError(string.Format(errorMessageFormat, $"the parent directory of output file {filePath} is not exist.")));
                    return;
                }
                if (!HasWritePermissionOnDir(info.Directory))
                {
                    errors.Add(new ArgumentError(string.Format(errorMessageFormat, $"writing to the directory {info.Directory.FullName} is restricted.")));
                }
                if (File.Exists(filePath))
                {
                    errors.Add(new ArgumentError(string.Format(warningMessageFormat, $"the file {filePath} exists and would be overriden.")));
                }
            }
            else
            {
                if (!File.Exists(filePath))
                {
                    errors.Add(new ArgumentError(string.Format(errorMessageFormat, $"the specified file {filePath} does not exist.")));
                    return;
                }
                if (info.Attributes.HasFlag(FileAttributes.Directory))
                {
                    errors.Add(new ArgumentError(string.Format(errorMessageFormat, $"the specified file {filePath} is directory.")));
                    return;
                }
                if (info.Attributes.HasFlag(FileAttributes.ReadOnly))
                {
                    errors.Add(new ArgumentError(string.Format(errorMessageFormat, $"the specified file {filePath} is read-only.")));
                }
            }
        }

        /// <summary>
        /// Checks if directory hat write permission
        /// </summary>
        /// <param name="dir">The directory for checking</param>
        /// <returns>True if has write permission, false otherwise</returns>
        public static bool HasWritePermissionOnDir(DirectoryInfo dir)
        {
            var writeAllow = false;
            var writeDeny = false;
            var accessControlList = dir.GetAccessControl();
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
