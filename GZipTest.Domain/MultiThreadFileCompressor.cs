using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Threading;
using GZipTest.Domain.Extensions;
using GZipTest.Utils;

namespace GZipTest.Domain
{
    public delegate void ProcessChunkDelegate(byte[] data, long order);

    /// <summary>
    /// File compressor that used threads for parallel processing of chucks of file
    /// </summary>
    public class MultiThreadFileCompressor : IFileCompressor
    {
        /// <summary>
        /// Number of bytes in Mb <value>1000000</value>
        /// </summary>
        private const int BytesInMb = 1000000;

        /// <summary>
        /// Size of one data chuck in bytes
        /// </summary>
        private const int ChuckSize = BytesInMb;

        /// <summary>
        /// Buffer size using for decompression
        /// </summary>
        private const int BufferSize = ChuckSize * 3;

        /// <summary>
        /// The maximum number of working threads
        /// </summary>
        private const int ThreadsCount = 8;

        /// <summary>
        /// Synchronization for threads queue
        /// </summary>
        private static readonly AutoResetEvent _waitThreadQueue = new AutoResetEvent(false);

        /// <summary>
        /// Synchronization for completing process
        /// </summary>
        private static readonly AutoResetEvent _waitProcessComplete = new AutoResetEvent(false);

        /// <summary>
        /// Locker object
        /// </summary>
        private readonly object _locker = new object();

        /// <summary>
        /// Delegate for processing data chunk
        /// </summary>
        private readonly ProcessChunkDelegate _processChunk;

        /// <summary>
        /// The path to source file
        /// </summary>
        private readonly string _sourceFilePath;

        /// <summary>
        /// The path to output file
        /// </summary>
        private readonly string _outputFilePath;

        /// <summary>
        /// The bytes array that represents the beginning of compressed data chunk
        /// </summary>
        private readonly byte[] _gzipChunkHeader = new byte[] { 0x1F, 0x8B, 0x08, 0x00, 0x00, 0x00, 0x00, 0x00 };

        /// <summary>
        /// Compressor option
        /// </summary>
        private readonly CompressorOption _option;

        /// <summary>
        /// The mapping for written data chunks
        /// </summary>
        private Dictionary<long, int> _writtenDataMap = new Dictionary<long, int>();

        /// <summary>
        /// The number of working threads
        /// </summary>
        private int _workingThreadsCount;

        private bool _isWaiting;

        /// <summary>
        /// Disposed flag
        /// </summary>
        private bool _disposedValue;

        /// <summary>
        /// Initializes a new instance of <see cref="MultiThreadFileCompressor"/>
        /// </summary>
        /// <param name="sourceFilePath">The path to file to be processed</param>
        /// <param name="outputFilePath">The path to output file</param>
        /// <param name="option">An option used to decide if compression or decompression is needed</param>
        public MultiThreadFileCompressor(string sourceFilePath, string outputFilePath, CompressorOption option)
        {
            if (string.IsNullOrWhiteSpace(sourceFilePath))
            {
                throw new ArgumentNullException(
                    $"Parameter {nameof(sourceFilePath)} is null or contains only white speces");
            }

            if (string.IsNullOrWhiteSpace(outputFilePath))
            {
                throw new ArgumentNullException(
                    $"Parameter {nameof(sourceFilePath)} is null or contains only white speces");
            }

            if (!FileValidator.ValidateFileExists(sourceFilePath))
            {
                throw new FileNotFoundException(
                    $"Specified file {sourceFilePath} does not exist");
            }

            if (FileValidator.ValidateFileIsReadOnly(sourceFilePath))
            {
                throw new UnauthorizedAccessException(
                    $"The specified file {sourceFilePath} is read-only");
            }

            if (FileValidator.ValidateFileExists(outputFilePath) &&
                FileValidator.ValidateFileIsReadOnly(outputFilePath))
            {
                throw new UnauthorizedAccessException(
                    $"The specified file {outputFilePath} is read-only");
            }

            if (!FileValidator.ValidateWritePermissionToDirectory(new FileInfo(outputFilePath).DirectoryName))
            {
                throw new UnauthorizedAccessException(
                    $"You do not have write permission to the parent directory of file {outputFilePath}");
            }

            if (option == CompressorOption.Decompress && !FileValidator.ValidateFileExtension(sourceFilePath,".gz"))
            {
                throw new ArgumentException(
                    $"The file {sourceFilePath} has not supported extension. Supported extensions: .gz");
            }

            _sourceFilePath = sourceFilePath;
            _outputFilePath = outputFilePath;
            _option = option;

            _processChunk = ProcessChunk;
        }

        /// <inheritdoc />
        public void ProcessFile()
        {
            var fileSize = new FileInfo(_sourceFilePath).Length;

            var chunksCount = GetChunksCount(fileSize);

            if (_option == CompressorOption.Compress)
            {
                CompressFile(chunksCount);
            }
            else
            {
                DecompressFile(chunksCount);
            }
        }

        /// <summary>
        /// Compresses data using GZip compression
        /// </summary>
        /// <param name="data">Bytes array of data</param>
        /// <param name="order">The order of array in process</param>
        private void CompressData(byte[] data, long order)
        {
            byte[] outputDataArray;
            using (var tempStream = new MemoryStream())
            {
                using (var compStream = new GZipStream(tempStream, CompressionMode.Compress))
                {
                    compStream.Write(data, 0, data.Length);
                }

                outputDataArray = tempStream.ToArray();

            }

            lock (_locker)
            {
                var address = GetAddressByOrder(order);
                var fileMode = _writtenDataMap.Any() ? FileMode.Open : FileMode.Create;
                using (var outputStream = new FileStream(_outputFilePath, fileMode, FileAccess.ReadWrite))
                {
                    outputStream.Insert(address, outputDataArray);
                }

                _writtenDataMap.Add(order, outputDataArray.Length);
                outputDataArray = null;
            }
        }

        /// <summary>
        /// Decompresses data using GZip compression
        /// </summary>
        /// <param name="data">Bytes array of data</param>
        /// <param name="order">The order of array in process</param>
        private void DecompressData(byte[] data, long order)
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

            lock (_locker)
            {
                var address = GetAddressByOrder(order);
                var fileMode = _writtenDataMap.Any() ? FileMode.Open : FileMode.Create;
                using (var outputStream = new FileStream(_outputFilePath, fileMode, FileAccess.ReadWrite))
                {
                    outputStream.Insert(address, outputDataArray);
                }

                _writtenDataMap.Add(order, outputDataArray.Length);
                outputDataArray = null;
            }
        }

        /// <summary>
        /// Decompresses file
        /// </summary>
        /// <param name="buffersCount">The number of buffers file would be split to</param>
        private void DecompressFile(long buffersCount)
        {
            List<byte> tail = new List<byte>();
            using (var inputStream = new FileStream(_sourceFilePath, FileMode.Open))
            {
                long order = 0;
                var foundAddresses = new List<int>();
                int tailSize = 0;
                long bufferNum = 0;
                while (bufferNum < buffersCount)
                {
                    bool lastBuffer = bufferNum == buffersCount - 1;

                    int bufferSize = GetChunkSize(inputStream.Length, buffersCount, bufferNum, BufferSize);
                    byte[] buffer = new byte[bufferSize];
                    inputStream.Read(buffer, 0, bufferSize);

                    foundAddresses = buffer.FindEntries(_gzipChunkHeader);

                    byte[] currentBuffer;
                    if (tail.Any())
                    {
                        //if there is tail from previous iteration - concat it with new buffer
                        currentBuffer = new byte[tail.Count + buffer.Length];
                        tail.ToArray().CopyTo(currentBuffer, 0);
                        Array.Copy(buffer, 0, currentBuffer, tail.Count, buffer.Length);
                        
                        foundAddresses.Insert(0, 0);
                        tailSize = tail.Count;
                        buffer = null;
                        tail.Clear();
                    }
                    else
                    {
                        currentBuffer = buffer;
                    }

                    if (foundAddresses.Count <= 1 && !lastBuffer)
                    {
                        tail = currentBuffer.ToList();
                        bufferNum++;
                        continue;
                    }

                    while (foundAddresses.Count > 1 || (foundAddresses.Count == 1 && lastBuffer))
                    {
                        if (_workingThreadsCount == ThreadsCount)
                        {
                            _waitThreadQueue.WaitOne();
                        }

                        int begin = foundAddresses.First();
                        begin += begin == 0 ? 0 : tailSize;
                        int end = foundAddresses.Count == 1 && lastBuffer ? currentBuffer.Length : foundAddresses.Skip(1).First() + tailSize;
                        foundAddresses.RemoveAt(0);

                        _processChunk.BeginInvoke(currentBuffer.GetRange(begin, end - begin), order, OnCompleteProcessChunk,
                            buffersCount);

                        order++;

                        if (foundAddresses.Count == 1 && !lastBuffer)
                        {
                            tail = currentBuffer.GetRange(end, currentBuffer.Length - end).ToList();
                            break;
                        }
                    }

                    bufferNum++;
                    currentBuffer = null;
                }
            }

            while (_workingThreadsCount != 0)
            {
                Thread.Sleep(10);
            }
            //_waitProcessComplete.WaitOne();
        }

        /// <summary>
        /// Gets address of data chunk by its order
        /// </summary>
        /// <param name="order">The data chunk's order</param>
        /// <returns>The address of specified data in file stream</returns>
        private long GetAddressByOrder(long order)
        {
           var nextAddress = _writtenDataMap.OrderBy(dataChunk => dataChunk.Key).TakeWhile(dataChunk => dataChunk.Key < order)
                .Aggregate(0L, (address, dataChunk) => address += dataChunk.Value);

           return nextAddress;
        }

        /// <summary>
        /// Decompresses file
        /// </summary>
        /// <param name="chunksCount">The number of data chunks file would be split to</param>
        private void CompressFile(long chunksCount)
        {
            using (var inputStream = new FileStream(_sourceFilePath, FileMode.Open))
            {
                var chunkNum = 0L;
                while (chunkNum < chunksCount)
                {
                    int chunkSize;
                    if (chunkNum == chunksCount - 1)
                    {
                        chunkSize = (int)(inputStream.Length % ChuckSize) == 0
                            ? ChuckSize
                            : (int)inputStream.Length % ChuckSize;
                    }
                    else
                    {
                        chunkSize = ChuckSize;
                    }


                    if (_workingThreadsCount == ThreadsCount)
                    {
                        _waitThreadQueue.WaitOne();
                    }

                    var currentData = new byte[chunkSize];
                    inputStream.Read(currentData, 0, currentData.Length);

                    _processChunk.BeginInvoke(currentData, chunkNum, OnCompleteProcessChunk, chunksCount);

                    chunkNum++;
                    currentData = null;
                }
            }

            _waitProcessComplete.WaitOne();
        }

        /// <summary>
        /// Callback method that is invoked after working thread complete
        /// </summary>
        /// <param name="asyncResult"></param>
        private void OnCompleteProcessChunk(IAsyncResult asyncResult)
        {
            Interlocked.Decrement(ref _workingThreadsCount);

            if (_isWaiting && _workingThreadsCount < ThreadsCount)
            {
                _waitThreadQueue.Set();
            }

            if (_writtenDataMap.Count == (long)asyncResult.AsyncState)
            {
                _waitProcessComplete.Set();
            }
        }

        /// <summary>
        /// Processes data chunk 
        /// </summary>
        /// <param name="data">Bytes array of data</param>
        /// <param name="order">The data chunk's order</param>
        private void ProcessChunk(byte[] data, long order)
        {
            Interlocked.Increment(ref _workingThreadsCount);

            if (!_isWaiting && _workingThreadsCount == ThreadsCount) {
                _waitThreadQueue.Reset();
                _isWaiting = true;
            }

            if (_option == CompressorOption.Compress)
            {
                CompressData(data, order);
            }
            else
            {
                DecompressData(data, order);
            }

            data = null;
        }

        /// <summary>
        /// Gets count of chunks depends in file size
        /// </summary>
        /// <param name="fileSize">The size of file</param>
        /// <returns>The number of data chunks</returns>
        private long GetChunksCount(long fileSize)
        {
            int divider = _option == CompressorOption.Compress ? ChuckSize : BufferSize;
            var chunksCount = fileSize / divider;
            if (fileSize % divider > 0)
            {
                chunksCount++;
            }

            return chunksCount;
        }

        /// <summary>
        /// Gets data chunk size
        /// </summary>
        /// <param name="fileSize">Size of file that would be divided to chunks</param>
        /// <param name="chunksCount">Count of data chunks</param>
        /// <param name="chunkIndex">Current chunk index</param>
        /// <param name="defaultSize">Default size used for chunk</param>
        /// <returns>Size of data chunk</returns>
        private int GetChunkSize(long fileSize, long chunksCount, long chunkIndex, int defaultSize)
        {
            int chunkSize;

            if (chunkIndex == chunksCount - 1)
            {
                chunkSize = (int)(fileSize % defaultSize) == 0
                    ? defaultSize
                    : (int)(fileSize % defaultSize);
            }
            else
            {
                chunkSize = defaultSize;
            }

            return chunkSize;
        }

        protected virtual void Dispose(bool disposing)
        {
            if (!_disposedValue)
            {
                if (disposing)
                {
                    _writtenDataMap = null;
                }

                _disposedValue = true;
            }
        }

        /// <inheritdoc />
        public void Dispose()
        {
            Dispose(disposing: true);
            GC.SuppressFinalize(this);
        }
    }
}