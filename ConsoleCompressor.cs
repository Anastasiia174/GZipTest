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

            CommandLine.Parser.Default.ParseArguments<CompressOptions, DecompressOptions>(args)
                .MapResult(
                    (CompressOptions opts) => RunCompressAndReturnExitCode(opts),
                    (DecompressOptions opts) => RunDecompressAndReturnExitCode(opts),
                    errs => 1
                );
        }

        private static int RunDecompressAndReturnExitCode(DecompressOptions opts)
        {
            throw new NotImplementedException();
        }

        private static int RunCompressAndReturnExitCode(CompressOptions opts)
        {
            throw new NotImplementedException();
        }
    }
}
