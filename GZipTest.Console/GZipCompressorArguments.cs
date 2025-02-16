﻿using CommandLine;

namespace GZipTest.Console
{
    public class GZipCompressorArguments
    {
        [Option('p', "path", Required = true, HelpText = "Path to the file to be processed")]
        public string SourceFilePath { get; set; }

        [Option('o', "output", Required = true, HelpText = "Path to the output file")]
        public string OutputFilePath { get; set; }
    }

    [Verb("compress", HelpText = "Compress specified file")]
    public class CompressOptions : GZipCompressorArguments
    {
    }

    [Verb("decompress", HelpText = "Decompress specified file")]
    public class DecompressOptions : GZipCompressorArguments
    {
    }
}