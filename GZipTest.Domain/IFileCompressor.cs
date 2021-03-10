namespace GZipTest.Domain
{
    /// <summary>
    /// File compressor interface
    /// </summary>
    public interface IFileCompressor
    {
        /// <summary>
        /// Process specified file
        /// </summary>
        void ProcessFile();
    }
}