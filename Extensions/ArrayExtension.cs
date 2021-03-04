using System;

namespace GZipTest.Extensions
{
    /// <summary>
    /// Extensions methods for <see cref="Array"/>
    /// </summary>
    public static class ArrayExtension
    {
        /// <summary>
        /// Gets range from specified array
        /// </summary>
        /// <typeparam name="T">Type of array elements</typeparam>
        /// <param name="array">Source array</param>
        /// <param name="start">Start index for getting range</param>
        /// <param name="length">Length of elements</param>
        /// <returns>New array that contains elements from source array</returns>
        public static T[] GetRange<T>(this T[] array, int start, int length)
        {
            if (length > array.Length)
            {
                throw new ArgumentException($"The specified length {length} is greater than array length {array.Length}");
            }

            var result = new T[length];

            Array.Copy(array, start, result, 0, length);

            return result;
        }
    }
}