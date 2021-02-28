using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Threading;

namespace GZipTest
{
    public delegate void ProcessFileDelegate(byte[] data, long order);

    /// <summary>
    /// File compressor that used threads for parallel processing of chucks of file
    /// </summary>
    public class MultiThreadFileCompressor : IFileCompressor, IDisposable
    {
        private const int BytesInMb = 1000000;

        /// <summary>
        /// Size of one data chuck in bytes
        /// </summary>
        private const int ChuckSize = BytesInMb;

        private const int ThreadsCount = 20;

        private static AutoResetEvent _waitThreadQueue = new AutoResetEvent(false);

        private static AutoResetEvent _waitProcessComplete = new AutoResetEvent(false);

        private object locker = new object();

        private int _workingThreadsCount;

        private bool _isWaiting;

        private bool _disposedValue;

        private FileStream _inputStream;

        private FileStream _outputStream;

        private ProcessFileDelegate _processFileAction;

        private Dictionary<long, int> _writtenDataMap = new Dictionary<long, int>();
        private string _sourceFilePath;
        private string _outputFilePath;

        public CompressorOption Option { get; set; }

        /// <inheritdoc />
        public void ProcessFile(string sourceFilePath, string outputFilePath)
        {
            var timer = Stopwatch.StartNew();
            if (!File.Exists(sourceFilePath))
            {
                throw new ArgumentException($"Specified file {sourceFilePath} does not exist.");
            }

            _sourceFilePath = sourceFilePath;
            _outputFilePath = outputFilePath;

            _inputStream = new FileStream(sourceFilePath, FileMode.Open);

            _processFileAction = (data, order) =>
            {
                Interlocked.Increment(ref _workingThreadsCount);
                Thread.CurrentThread.Name = $"chunk {order} : {data.Length}";

                if (!_isWaiting && _workingThreadsCount == ThreadsCount)
                {
                    _waitThreadQueue.Reset();
                    _isWaiting = true;
                }

                if (Option == CompressorOption.Compress)
                {
                    CompressData(data, order);
                }
                else
                {
                    DecompressData(data);
                }
            };

            long fileSize = new FileInfo(sourceFilePath).Length;

            CheckIfFullFileCouldBeLoaded(fileSize / BytesInMb);

            var chunksCount = fileSize / ChuckSize;
            if (_inputStream.Length % ChuckSize > 0)
            {
                chunksCount++;
            }
            if (CheckIfFullFileCouldBeLoaded(chunksCount))
            {
                ProcessFullFile(chunksCount);
            }

            timer.Stop();
            Console.WriteLine($"Elapsed time = {timer.Elapsed.TotalSeconds}");
        }

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

            lock (locker)
            {
                var address = GetAddressByOrder(order);
                using (var outputStream = new FileStream("m-" + _outputFilePath, FileMode.OpenOrCreate, FileAccess.ReadWrite))
                {
                    InsertIntoFile(outputStream, address, outputDataArray);
                }

                _writtenDataMap.Add(order, outputDataArray.Length);
            }
        }

        public static void InsertIntoFile(FileStream stream, long offset, byte[] extraBytes)
        {
            if (offset < 0 || offset > stream.Length)
            {
                throw new ArgumentOutOfRangeException("Offset is out of range");
            }
            const int maxBufferSize = 10000000;
            int bufferSize = maxBufferSize;
            long temp = stream.Length - offset;
            if (temp <= maxBufferSize)
            {
                bufferSize = (int)temp;
            }
            byte[] buffer = new byte[bufferSize];
            long currentPositionToRead = stream.Length;
            int numberOfBytesToRead;
            while (true)
            {
                numberOfBytesToRead = bufferSize;
                temp = currentPositionToRead - offset;
                if (temp < bufferSize)
                {
                    numberOfBytesToRead = (int)temp;
                }
                currentPositionToRead -= numberOfBytesToRead;
                stream.Position = currentPositionToRead;
                stream.Read(buffer, 0, numberOfBytesToRead);
                stream.Position = currentPositionToRead + extraBytes.Length;
                stream.Write(buffer, 0, numberOfBytesToRead);
                if (temp <= bufferSize)
                {
                    break;
                }
            }
            stream.Position = offset;
            stream.Write(extraBytes, 0, extraBytes.Length);
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

        private long GetAddressByOrder(long order)
        {
           var nextAddress = _writtenDataMap.OrderBy(dataChunk => dataChunk.Key).TakeWhile(dataChunk => dataChunk.Key < order)
                .Aggregate(0, (address, dataChunk) => address += dataChunk.Value);
           //nextAddress = nextAddress == 0 ? nextAddress : nextAddress + 1;

           return nextAddress;
        }

        private void ProcessFullFile(long chunksCount)
        {
            var dataMap = new Dictionary<long, byte[]>();
            for (long i = 1; i <= chunksCount; i++)
            {
                var arrayLength = i != chunksCount ? ChuckSize : (int)(_inputStream.Length % ChuckSize) == 0 ? ChuckSize : _inputStream.Length % ChuckSize;
                var chuckData = new byte[arrayLength];
                _inputStream.Read(chuckData, 0, (int)arrayLength);
                dataMap.Add(i, chuckData);
            }

            while (dataMap.Count != 0)
            {
                if (_workingThreadsCount == ThreadsCount)
                {
                    _waitThreadQueue.WaitOne();
                }

                var currentData = dataMap.First();
                _processFileAction.BeginInvoke(currentData.Value, currentData.Key, OnCompleteProcessChunk, chunksCount);
                dataMap.Remove(currentData.Key);
            }

            _waitProcessComplete.WaitOne();
        }

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