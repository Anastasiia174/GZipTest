using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Threading;
using GZipTest.Extensions;

namespace GZipTest
{
    public delegate void ProcessChunkDelegate(byte[] data, long order);

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

        private static readonly AutoResetEvent _waitThreadQueue = new AutoResetEvent(false);

        private static readonly AutoResetEvent _waitProcessComplete = new AutoResetEvent(false);

        private readonly object locker = new object();

        private readonly Dictionary<long, int> _writtenDataMap = new Dictionary<long, int>();

        private int _workingThreadsCount;

        private bool _isWaiting;

        private bool _disposedValue;

        private ProcessChunkDelegate _processChunk;

        private string _sourceFilePath;

        private string _outputFilePath;

        private long _fileSize;

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

            _fileSize = new FileInfo(sourceFilePath).Length;

            //_inputStream = new FileStream(sourceFilePath, FileMode.Open);

            _processChunk = ProcessChunk;

            var chunksCount = _fileSize / ChuckSize;
            if (_fileSize % ChuckSize > 0)
            {
                chunksCount++;
            }

            ProcessFileIteratively(chunksCount);
            //var fullFileLoad = CheckIfFullFileCouldBeLoaded(fileSize / BytesInMb);
            //if (fullFileLoad)
            //{
            //    ProcessFullFile(chunksCount);
            //}
            //else
            //{
            //    ProcessFileIteratively(chunksCount);
            //}
            

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
                    outputStream.Insert(address, outputDataArray);
                    //InsertIntoFile(outputStream, address, outputDataArray);
                }

                _writtenDataMap.Add(order, outputDataArray.Length);
                outputDataArray = null;
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
            //_outputStream.Write(outputDataArray, 0, outputDataArray.Length);
        }

        private bool CheckIfFullFileCouldBeLoaded(long fileSize)
        {
            var ramCounter = new PerformanceCounter("Memory", "Available MBytes");
            return fileSize < ramCounter.NextValue() / 2;
        }

        private long GetAddressByOrder(long order)
        {
           var nextAddress = _writtenDataMap.OrderBy(dataChunk => dataChunk.Key).TakeWhile(dataChunk => dataChunk.Key < order)
                .Aggregate(0L, (address, dataChunk) => address += dataChunk.Value);
           //nextAddress = nextAddress == 0 ? nextAddress : nextAddress + 1;

           return nextAddress;
        }

        private void ProcessFullFile(long chunksCount)
        {
            var dataMap = new Dictionary<long, byte[]>();
            using (var inputStream = new FileStream(_sourceFilePath, FileMode.Open))
            {
                for (long i = 1; i <= chunksCount; i++)
                {
                    var arrayLength = i != chunksCount ? ChuckSize : (int)(inputStream.Length % ChuckSize) == 0 ? ChuckSize : inputStream.Length % ChuckSize;
                    var chuckData = new byte[arrayLength];
                    inputStream.Read(chuckData, 0, (int)arrayLength);
                    dataMap.Add(i, chuckData);
                }
            }

            while (dataMap.Count != 0)
            {
                if (_workingThreadsCount == ThreadsCount)
                {
                    _waitThreadQueue.WaitOne();
                }

                var currentData = dataMap.First();
                _processChunk.BeginInvoke(currentData.Value, currentData.Key, OnCompleteProcessChunk, chunksCount);
                dataMap.Remove(currentData.Key);
            }

            _waitProcessComplete.WaitOne();
        }

        private void ProcessFileIteratively(long chunksCount)
        {
            //var chunksInfoMap = new List<(long order, long address, int size)>();
            var chunksInfoMap = new Dictionary<long, int>();
            for (long i = 0; i < chunksCount; i++)
            {
                int chunkSize;
                //long chunkAddress = i * ChuckSize;

                if (i == chunksCount - 1)
                {
                    chunkSize = (int) (_fileSize % ChuckSize) == 0
                        ? ChuckSize
                        : (int) _fileSize % ChuckSize;
                }
                else
                {
                    chunkSize = ChuckSize;
                }
                
                //chunksInfoMap.Add( (i, chunkAddress, chunkSize) );
                chunksInfoMap.Add(i, chunkSize);
            }

            using (var inputStream = new FileStream(_sourceFilePath, FileMode.Open))
            {
                while (chunksInfoMap.Count != 0)
                {
                    if (_workingThreadsCount == ThreadsCount)
                    {
                        _waitThreadQueue.WaitOne();
                    }

                    var currentChunkInfo = chunksInfoMap.First();
                    var currentData = new byte[currentChunkInfo.Value];

                    //inputStream.Position = currentChunkInfo.address;
                    inputStream.Read(currentData, 0, currentData.Length);

                    _processChunk.BeginInvoke(currentData, currentChunkInfo.Key, OnCompleteProcessChunk, chunksCount);
                    chunksInfoMap.Remove(currentChunkInfo.Key);

                    currentData = null;
                }
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

        private void ProcessChunk(byte[] data, long order)
        {
            Interlocked.Increment(ref _workingThreadsCount);
            Thread.CurrentThread.Name = $"chunk {order} : {data.Length}";

            if (!_isWaiting && _workingThreadsCount == ThreadsCount) {
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