using System;
using System.IO;

namespace GZipTest.Extensions
{
    public static class FileStreamExtension
    {
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
