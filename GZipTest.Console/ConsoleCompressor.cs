using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security.AccessControl;
using CommandLine;
using GZipTest.Domain;

namespace GZipTest.Console
{
    class ConsoleCompressor
    {
        private const string Title = "GZip test tool for compressing files";

        static void Main(string[] args)
        {
            System.Console.Title = Title;

            CommandLine.Parser.Default.ParseArguments<CompressOptions, DecompressOptions>(args)
                .MapResult(
                    (CompressOptions opts) => RunCompressAndReturnExitCode(opts),
                    (DecompressOptions opts) => RunDecompressAndReturnExitCode(opts),
                    errs => 1
                );
        }

        /// <summary>
        /// Runs decompression process
        /// </summary>
        /// <param name="opts">Decompression options</param>
        /// <returns>0 if success, 1 otherwise</returns>
        private static int RunDecompressAndReturnExitCode(DecompressOptions opts)
        {
            return ProcessFile(opts.SourceFilePath, opts.OutputFilePath, CompressorOption.Decompress);
        }

        /// <summary>
        /// Runs compression process
        /// </summary>
        /// <param name="opts">Compression options</param>
        /// <returns>0 if success, 1 otherwise</returns>
        private static int RunCompressAndReturnExitCode(CompressOptions opts)
        {
            return ProcessFile(opts.SourceFilePath, opts.OutputFilePath, CompressorOption.Compress);
        }

        private static int ProcessFile(string sourceFilePath, string outputFilePath, CompressorOption option)
        {
            var timer = Stopwatch.StartNew();
            int result = 0;

            try
            {
                using (IFileCompressor fileCompressor = new MultiThreadFileCompressor(sourceFilePath, outputFilePath, option))
                {
                    fileCompressor.ProcessFile();
                }
            }
            catch (Exception e)
            {
                System.Console.WriteLine(e);
                result = 1;
            }

            timer.Stop();
            if (result == 0)
            {
                System.Console.WriteLine($"Elapsed time = {timer.Elapsed.TotalSeconds}");
            }
            
            return result;
        }
    }
}
