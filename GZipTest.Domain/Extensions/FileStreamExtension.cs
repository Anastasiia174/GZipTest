﻿using System;
using System.IO;

namespace GZipTest.Domain.Extensions
{
    /// <summary>
    /// Extension methods for <see cref="FileStream"/> class
    /// </summary>
    internal static class FileStreamExtension
    {
        /// <summary>
        /// Inserts specified data array to specified offset in stream
        /// </summary>
        /// <param name="stream">File stream to insert offset to</param>
        /// <param name="offset">The zero-based byte offset in stream from which to begin copying bytes to the stream. </param>
        /// <param name="extraBytes">The buffer containing data to write to the stream.</param>
        public static void Insert(this FileStream stream, long offset, byte[] extraBytes)
        {
            if (offset < 0 || offset > stream.Length)
            {
                throw new ArgumentOutOfRangeException("Offset is out of range");
            }
            const int maxBufferSize = 100000000;
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
    }
}
