using System;

namespace Stratis.SCL.Base
{
    public static class Operations
    {
        public static void Noop() { }

        /// <summary>
        /// Unflattens an array of size N * <paramref name="subArrayLength"/> into an array of N sub-arrays each of length <paramref name="subArrayLength"/>.
        /// </summary>
        /// <typeparam name="T">The type of the array elements.</typeparam>
        /// <param name="array">The array to convert.</param>
        /// <param name="subArrayLength">The length of the sub-arrays to create.</param>
        /// <returns>An array of N sub-arrays each of length <paramref name="subArrayLength"/>.</returns>
        /// <remarks>The value of N is implied by the length of the input array and the value of <paramref name="subArrayLength"/>.</remarks>
        public static T[][] UnflattenArray<T>(T[] array, int subArrayLength) where T : struct
        {
            try
            {
                int cnt = array.Length / subArrayLength;

                if (array.Length != subArrayLength * cnt)
                    return null;

                var buffer = new T[cnt][];
                for (int i = 0; i < cnt; i++)
                {
                    buffer[i] = new T[subArrayLength];
                    Array.Copy(array, i * subArrayLength, buffer[i], 0, subArrayLength);
                }

                return buffer;
            }
            catch (Exception)
            {
                return null;
            }
        }

        /// <summary>
        /// Flattens an array of N sub-arrays each of length <paramref name="subArrayLength"/> into an array of size N * <paramref name="subArrayLength"/>.
        /// </summary>
        /// <typeparam name="T">The type of the array elements.</typeparam>
        /// <param name="array">The array to convert.</param>
        /// <param name="subArrayLength">The length of the sub-arrays.</param>
        /// <returns>An array of size N * <paramref name="subArrayLength"/></returns>
        public static T[] FlattenArray<T>(T[][] array, int subArrayLength) where T : struct
        {
            try
            {
                var buffer = new T[array.Length * subArrayLength];

                for (int j = 0; j < array.Length; j++)
                    Array.Copy(array[j], 0, buffer, j * subArrayLength, subArrayLength);

                return buffer;
            }
            catch (Exception)
            {
                return null;
            }
        }
    }
}
