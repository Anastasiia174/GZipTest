using System;
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
            compressor.Option = CompressorOption.Decompress;
            compressor.ProcessFile(opts.SourceFilePath, opts.OutputFilePath);

            return 0;
        }

        private static int RunCompressAndReturnExitCode(CompressOptions opts, IFileCompressor compressor)
        {
            compressor.ProcessFile(opts.SourceFilePath, opts.OutputFilePath);

            return 0;
        }
    }
}
