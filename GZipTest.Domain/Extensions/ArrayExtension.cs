using System;
using System.Collections.Generic;
using System.Linq;

namespace GZipTest.Domain.Extensions
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

        public static List<int> FindEntries<T>(this T[] array, T[] sequence)
        {
            if (sequence.Length > array.Length)
            {
                throw new ArgumentException(
                    $"Sequence length {sequence.Length} is more than source array length {array.Length}");
            }

            var result = new List<int>();

            for (int i = 0; i < array.Length; i++)
            {
                if (array[i].Equals(sequence[0]))
                {
                    if (Enumerable.Range(1, sequence.Length - 1).All(j => (array.Length > i + j) && (array[i + j].Equals(sequence[j]))))
                    {
                        result.Add(i);
                    }
                }
            }

            return result;
        }
    }
}