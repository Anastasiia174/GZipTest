using System;
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

        private const int ThreadsCount = 20;

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

            //_inputStream = new FileStream(sourceFilePath, FileMode.Open);

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
            string gzipHeaderString = string.Join(string.Empty, gzipChunkHeader.Select(b => b.ToString("X2")));

            List<byte> tail = new List<byte>();
            int? firstAddress = null;
            int tailSize = 0;
            
            using (var inputStream = new FileStream(_sourceFilePath, FileMode.Open))
            {
                long order = 0;
                long chunksCount = inputStream.Length / BufferSize;
                if (inputStream.Length % BufferSize > 0)
                {
                    chunksCount++;
                }

                //var foundAddresses = new List<int>();
                for (long bufferNum = 0; bufferNum < chunksCount; bufferNum++)
                {
                    bool lastBuffer = bufferNum == chunksCount - 1;

                    byte[] buffer = new byte[BufferSize];
                    inputStream.Read(buffer, 0, BufferSize);

                    var bufferString = string.Join(string.Empty, buffer.Select(b => b.ToString("X2")));

                    var chunkBeginnings = Regex.Matches(bufferString, gzipHeaderString);
                    bufferString = null;

                    

                    //int address = 0;
                    //int offset = 0;
                    //long order = 0;
                    //while (offset <= bufferSize - gzipChunkHeader.Length)
                    //{
                    //    if (buffer.Skip(offset).Take(gzipChunkHeader.Length).SequenceEqual(gzipChunkHeader))
                    //    {
                    //        order = bufferNum * bufferSize + offset;
                    //        address = offset;
                    //        foundAddresses.Add(address);
                    //    }

                    //    offset += gzipChunkHeader.Length;
                    //}

                    //while (foundAddresses.Count > 1)
                    //{
                    //    if (_workingThreadsCount == ThreadsCount)
                    //    {
                    //        _waitThreadQueue.WaitOne();
                    //    }

                    //    _processChunk.BeginInvoke(buffer.GetRange(foundAddresses[0], foundAddresses[1]), order,
                    //        OnCompleteProcessChunk, null);
                    //    foundAddresses.RemoveAt(0);
                    //    foundAddresses.RemoveAt(1);
                    //}

                    byte[] currentBuffer;
                    if (tail != null && tail.Any())
                    {
                        //if there is tail from previous iteration - concat it with new buffer
                        currentBuffer = new byte[tail.Count + buffer.Length];
                        tail.ToArray().CopyTo(currentBuffer, 0);
                        Array.Copy(buffer, 0, currentBuffer, tail.Count, buffer.Length);
                        tailSize = tail.Count;
                        buffer = null;
                        tail = null;
                    }
                    else
                    {
                        currentBuffer = buffer;
                    }

                    if (chunkBeginnings.Count == 0 && !lastBuffer)
                    {
                        tail = currentBuffer.ToList();
                        continue;
                    }

                    int index = 0;
                    while (index < chunkBeginnings.Count || lastBuffer)
                    {
                        if (index == chunkBeginnings.Count - 1 && !firstAddress.HasValue)
                        {
                            //if the last address - take the rest of buffer for next iteration
                            int lastPosition = chunkBeginnings[index].Index / 2 + tailSize;
                            tail = currentBuffer.GetRange(lastPosition, currentBuffer.Length - lastPosition).ToList();
                            firstAddress = 0;
                            break;
                        }

                        if (_workingThreadsCount == ThreadsCount)
                        {
                            _waitThreadQueue.WaitOne();
                        }

                        var begin = index == 0 && firstAddress.HasValue ? firstAddress.Value : chunkBeginnings[index].Index / 2 + tailSize;
                        var end = lastBuffer ? currentBuffer.Length : chunkBeginnings[index == 0 && firstAddress.HasValue ? index : index + 1].Index / 2 + tailSize;

                        _processChunk.BeginInvoke(currentBuffer.GetRange(begin, end - begin), order,
                            OnCompleteProcessChunk, chunksCount);
                        //_processChunk.Invoke(currentBuffer.GetRange(begin, end - begin), order);

                        if (index == 0 && firstAddress.HasValue)
                        {
                            firstAddress = null;
                        }
                        else
                        {
                            index++;
                        }

                        order++;
                        lastBuffer = false;
                    }

                    currentBuffer = null;
                    tailSize = 0;
                }
            }

            _waitProcessComplete.WaitOne();
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
            //var chunksInfoMap = new Dictionary<long, int>();
            //for (long i = 0; i < chunksCount; i++)
            //{
            //    int chunkSize;
            //    //long chunkAddress = i * ChuckSize;

            //    if (i == chunksCount - 1)
            //    {
            //        chunkSize = (int) (_fileSize % ChuckSize) == 0
            //            ? ChuckSize
            //            : (int) _fileSize % ChuckSize;
            //    }
            //    else
            //    {
            //        chunkSize = ChuckSize;
            //    }
                
            //    //chunksInfoMap.Add( (i, chunkAddress, chunkSize) );
            //    chunksInfoMap.Add(i, chunkSize);
            //}

            using (var inputStream = new FileStream(_sourceFilePath, FileMode.Open))
            {
                var chunkNum = 0L;
                while (chunkNum < chunksCount)
                //while (chunksInfoMap.Count != 0)
                {
                    int chunkSize;
                    //long chunkAddress = i * ChuckSize;

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

                    //var currentChunkInfo = chunksInfoMap.First();
                    var currentData = new byte[chunkSize];

                    //inputStream.Position = currentChunkInfo.address;
                    inputStream.Read(currentData, 0, currentData.Length);

                    _processChunk.BeginInvoke(currentData, chunkNum, OnCompleteProcessChunk, chunksCount);
                    //chunksInfoMap.Remove(currentChunkInfo.Key);

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