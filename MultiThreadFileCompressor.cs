﻿using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text.RegularExpressions;
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

        private const int BufferSize = ChuckSize * 3;

        private const int ThreadsCount = 4;

        private static readonly AutoResetEvent _waitThreadQueue = new AutoResetEvent(false);

        private static readonly AutoResetEvent _waitProcessComplete = new AutoResetEvent(false);

        private readonly object _locker = new object();

        private Dictionary<long, int> _writtenDataMap = new Dictionary<long, int>();

        private int _workingThreadsCount;

        private bool _isWaiting;

        private bool _disposedValue;

        private ProcessChunkDelegate _processChunk;

        private string _sourceFilePath;

        private string _outputFilePath;

        private long _fileSize;

        /// <inheritdoc />
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

            _processChunk = ProcessChunk;

            var chunksCount = _fileSize / ChuckSize;
            if (_fileSize % ChuckSize > 0)
            {
                chunksCount++;
            }

            chunksCount = GetChunksCount(_fileSize);

            if (Option == CompressorOption.Compress)
            {
                ProcessFileIteratively(chunksCount);
            }
            else
            {
                DecompressFileIteratively();
            }

            timer.Stop();
            Console.WriteLine($"Elapsed time = {timer.Elapsed.TotalSeconds}");
        }

        private void CompressData(byte[] data, long order)
        {
            byte[] outputDataArray;
            using (var tempStream = new MemoryStream())
            {
                using (var compStream = new GZipStream(tempStream,
                    Option == CompressorOption.Compress ? CompressionMode.Compress : CompressionMode.Decompress))
                {
                    compStream.Write(data, 0, data.Length);
                }

                outputDataArray = tempStream.ToArray();

            }

            lock (_locker)
            {
                var address = GetAddressByOrder(order);
                using (var outputStream = new FileStream("m-" + _outputFilePath, FileMode.OpenOrCreate, FileAccess.ReadWrite))
                {
                    outputStream.Insert(address, outputDataArray);
                }

                _writtenDataMap.Add(order, outputDataArray.Length);
                outputDataArray = null;
            }
        }

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
                using (var outputStream = new FileStream("m-" + _outputFilePath, fileMode, FileAccess.ReadWrite))
                {
                    outputStream.Insert(address, outputDataArray);
                }

                _writtenDataMap.Add(order, outputDataArray.Length);
                outputDataArray = null;
            }
        }

        private void DecompressFileIteratively()
        {
            byte[] gzipChunkHeader = new byte[] { 0x1F, 0x8B, 0x08, 0x00, 0x00, 0x00, 0x00, 0x00 };

            List<byte> tail = new List<byte>();

            using (var inputStream = new FileStream(_sourceFilePath, FileMode.Open))
            {
                long order = 0;
                long buffersCount = inputStream.Length / BufferSize;
                if (inputStream.Length % BufferSize > 0)
                {
                    buffersCount++;
                }

                var foundAddresses = new List<int>();
                int tailSize = 0;
                long bufferNum = 0;
                while (bufferNum < buffersCount)
                {
                    bool lastBuffer = bufferNum == buffersCount - 1;

                    int bufferSize = GetChunkSize(inputStream.Length, buffersCount, bufferNum, BufferSize);
                    byte[] buffer = new byte[bufferSize];
                    inputStream.Read(buffer, 0, bufferSize);

                    foundAddresses = buffer.FindEntries(gzipChunkHeader);

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
                        //_processChunk.Invoke(currentBuffer.GetRange(begin, end - begin), order);

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

            _waitProcessComplete.WaitOne();
        }

        private long GetAddressByOrder(long order)
        {
           var nextAddress = _writtenDataMap.OrderBy(dataChunk => dataChunk.Key).TakeWhile(dataChunk => dataChunk.Key < order)
                .Aggregate(0L, (address, dataChunk) => address += dataChunk.Value);
           //nextAddress = nextAddress == 0 ? nextAddress : nextAddress + 1;

           return nextAddress;
        }

        private void ProcessFileIteratively(long chunksCount)
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
            //Thread.CurrentThread.Name = $"chunk {order} : {data.Length}";

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
                DecompressData(data, order);
            }

            data = null;
        }

        private long GetChunksCount(long fileSize)
        {
            int divider = Option == CompressorOption.Compress ? ChuckSize : BufferSize;
            var chunksCount = fileSize / divider;
            if (fileSize % divider > 0)
            {
                chunksCount++;
            }

            return chunksCount;
        }

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

        public void Dispose()
        {
            // Do not change this code. Put cleanup code in 'Dispose(bool disposing)' method
            Dispose(disposing: true);
            GC.SuppressFinalize(this);
        }
    }
}