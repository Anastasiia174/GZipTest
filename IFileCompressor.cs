namespace GZipTest
{
    public interface IFileCompressor
    {
        /// <summary>
        /// An option used to decide if compression or decompression is needed
        /// </summary>
        public CompressorOption Option { get; set; }

        /// <summary>
        /// Process specified file
        /// </summary>
        /// <param name="sourceFilePath">The path to file to be processed</param>
        /// <param name="outputFilePath">The path to output file</param>
        void ProcessFile(string sourceFilePath, string outputFilePath);
    }
}