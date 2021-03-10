using System;
using System.Diagnostics;
using System.IO;
using CommandLine;
using GZipTest.Domain;

namespace GZipTest.Console
{
    /// <summary>
    /// Console tool for compressing files
    /// </summary>
    class ConsoleCompressor
    {
        /// <summary>
        /// Console title
        /// </summary>
        private const string Title = "GZip test tool for compressing files";

        /// <summary>
        /// Entry point for application
        /// </summary>
        /// <param name="args"></param>
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

        /// <summary>
        /// Performs file processing 
        /// </summary>
        /// <param name="sourceFilePath">The path to file to be processed</param>
        /// <param name="outputFilePath">The path to output file</param>
        /// <param name="option">An option used to decide if compression or decompression is needed</param>
        /// <returns></returns>
        private static int ProcessFile(string sourceFilePath, string outputFilePath, CompressorOption option)
        {
            var timer = Stopwatch.StartNew();
            int result = 0;

            if (File.Exists(outputFilePath))
            {
                System.Console.WriteLine(
                    $"Warning: specified output file {outputFilePath} exists and would be overwritten");
            }

            try
            {
                using (IFileCompressor fileCompressor = new MultiThreadFileCompressor(sourceFilePath, outputFilePath, option))
                {
                    fileCompressor.ProcessFile();
                }
            }
            catch (Exception e)
            {
                System.Console.WriteLine($"Error: {e.Message}");
                result = 1;
            }

            timer.Stop();
            if (result == 0)
            {
                System.Console.WriteLine($"File was processed in = {timer.Elapsed.TotalSeconds} s");
            }
            
            return result;
        }
    }
}
