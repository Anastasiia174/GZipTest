using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
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

        private static int RunDecompressAndReturnExitCode(DecompressOptions opts, IFileCompressor compressor)
        {
            var errors = new List<ArgumentError>();
            CheckFileAccess(opts.SourceFilePath, errors, false);
            CheckFileAccess(opts.OutputFilePath, errors, true);

            if (errors.Any())
            {
                errors.ForEach(Console.WriteLine);

                if (errors.Any(err => !err.IsWarning))
                {
                    return 1;
                }
            }

            compressor.Option = CompressorOption.Decompress;
            compressor.ProcessFile(opts.SourceFilePath, opts.OutputFilePath);

            return 0;
        }

        private static int RunCompressAndReturnExitCode(CompressOptions opts, IFileCompressor compressor)
        {
            var errors = new List<ArgumentError>();
            CheckFileAccess(opts.SourceFilePath, errors, false);
            CheckFileAccess(opts.OutputFilePath, errors, true);

            if (errors.Any())
            {
                errors.ForEach(Console.WriteLine);

                if (errors.Any(err => !err.IsWarning))
                {
                    return 1;
                }
            }

            compressor.ProcessFile(opts.SourceFilePath, opts.OutputFilePath);

            return 0;
        }

        private static void CheckFileAccess(string filePath, List<ArgumentError> errorMessages, bool isWriting)
        {
            errorMessages ??= new List<ArgumentError>();

            string errorMessageFormat = "Error: {0}";
            string warningMessageFormat = "Warning: {0}";

            var info = new FileInfo(filePath);
            if (isWriting)
            {
                if (!Directory.Exists(filePath))
                {
                    errorMessages.Add(new ArgumentError(string.Format(errorMessageFormat, $"the parent directory of file {filePath} is not exist.")));
                    return;
                }
                if (!HasWritePermissionOnDir(info.Directory))
                {
                    errorMessages.Add(new ArgumentError(string.Format(errorMessageFormat, $"writing to the directory {info.Directory.FullName} is restricted.")));
                }
                if (File.Exists(filePath))
                {
                    errorMessages.Add(new ArgumentError(string.Format(warningMessageFormat, $"the file {filePath} exists and would be overriden.")));
                }
            }
            else
            {
                if (!File.Exists(filePath))
                {
                    errorMessages.Add(new ArgumentError(string.Format(errorMessageFormat, $"the specified file {filePath} does not exist.")));
                    return;
                }
                if (info.Attributes.HasFlag(FileAttributes.Directory))
                {
                    errorMessages.Add(new ArgumentError(string.Format(errorMessageFormat, $"the specified file {filePath} is directory.")));
                    return;
                }
                if (info.Attributes.HasFlag(FileAttributes.ReadOnly))
                {
                    errorMessages.Add(new ArgumentError(string.Format(errorMessageFormat, $"the specified file {filePath} is read-only.")));
                }
            }
        }

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
