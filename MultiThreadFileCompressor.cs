using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Threading;

namespace GZipTest
{
    public delegate void ProcessFileDelegate(byte[] data);

    /// <summary>
    /// File compressor that used threads for parallel processing of chucks of file
    /// </summary>
    public class MultiThreadFileCompressor : IFileCompressor, IDisposable
    {
        /// <summary>
        /// Size of one data chuck in bytes
        /// </summary>
        private const int ChuckSize = 1000000;

        private const int ThreadsCount = 20;

        private int _workingThreadsCount;

        private bool _disposedValue;

        private FileStream _inputStream;

        private FileStream _outputStream;

        private ProcessFileDelegate _processFileAction;

        public CompressorOption Option { get; set; }

        /// <inheritdoc />
        public void ProcessFile(string sourceFilePath, string outputFilePath)
        {
            if (!File.Exists(sourceFilePath))
            {
                throw new ArgumentException($"Specified file {sourceFilePath} does not exist.");
            }
            //FileInfo outputFileInfo = new FileInfo(outputFilePath);
            //FileInfo inputFileInfo = new FileInfo(sourceFilePath);

            var attrs = File.GetAttributes(sourceFilePath);
            _inputStream = new FileStream(sourceFilePath, FileMode.Open);
            
            _outputStream = File.Create(outputFilePath);

            _processFileAction = data =>
            {
                Interlocked.Increment(ref _workingThreadsCount);

                if (Option == CompressorOption.Compress)
                {
                    CompressData(data);
                }
                else
                {
                    DecompressData(data);
                }
            };

            var chunksCount = _inputStream.Length / ChuckSize;
            if (_inputStream.Length % ChuckSize > 0)
            {
                chunksCount++;
            }
            if (CheckIfFullFileCouldBeLoaded(chunksCount))
            {
                ProcessFullFile(chunksCount);
            }
        }

        private void CompressData(byte[] data)
        {
            using (var tempStream = new MemoryStream())
            {
                using (var compStream = new GZipStream(tempStream, CompressionMode.Compress))
                {
                    compStream.Write(data, 0, data.Length);
                }

                var outputDataArray = tempStream.ToArray();
                _outputStream.Write(outputDataArray, 0, outputDataArray.Length);
            }
        }

        private void DecompressData(byte[] data)
        {
            byte[] outputDataArray;
            using (var inStream = new MemoryStream(data, 0, data.Length))
            {
                using (var decompStream = new GZipStream(inStream, CompressionMode.Decompress))
                {
                    using var outStream = new MemoryStream();
                    decompStream.CopyTo(outStream);
                    outputDataArray = outStream.ToArray();
                }
            }
            _outputStream.Write(outputDataArray, 0, outputDataArray.Length);
        }

        private bool CheckIfFullFileCouldBeLoaded(long fileSize)
        {
            var ramCounter = new PerformanceCounter("Memory", "Available MBytes");
            return fileSize < ramCounter.NextValue() / 2;
        }

        private void ProcessFullFile(long chunksCount)
        {
            var dataMap = new Dictionary<long, byte[]>();
            for (long i = 0; i <= chunksCount; i++)
            {
                var chuckData = i == chunksCount ? new byte[_inputStream.Length % ChuckSize] : new byte[ChuckSize];
                _inputStream.Read(chuckData, 0, i == chunksCount ? (int)(_inputStream.Length % ChuckSize) : ChuckSize);
                dataMap.Add(i, chuckData);
            }

            while (dataMap.Count != 0)
            {
                //if (_workingThreadsCount == ThreadsCount)
                //{

                //}

                var currentData = dataMap.First();
                //_processFileAction.BeginInvoke(currentData.Value, OnCompleteProcessChunk, null);
                _processFileAction.Invoke(currentData.Value);
                dataMap.Remove(currentData.Key);
            }
        }

        private void OnCompleteProcessChunk(IAsyncResult asyncResult)
        {
            Interlocked.Decrement(ref _workingThreadsCount);

        }

        protected virtual void Dispose(bool disposing)
        {
            if (!_disposedValue)
            {
                if (disposing)
                {
                    
                }

                _disposedValue = true;
            }
        }

        public void Dispose()
        {
            // Do not change this code. Put cleanup code in 'Dispose(bool disposing)' method
            Dispose(disposing: true);
            GC.SuppressFinalize(this);
        }
    }
}