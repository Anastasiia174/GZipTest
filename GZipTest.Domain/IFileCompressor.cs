using System;

namespace GZipTest.Domain
{
    /// <summary>
    /// File compressor interface
    /// </summary>
    public interface IFileCompressor : IDisposable
    {
        /// <summary>
        /// Process specified file
        /// </summary>
        void ProcessFile();
    }
}