using System;

namespace Stratis.Bitcoin.Utilities
{
    public class BinarySearch
    {
        /// <summary>
        /// Finds the first index in a range which evaluates to <c>true</c> when <paramref name="func"/> is applied to it.
        /// The range should strictly contain zero or more indexes for which <paramref name="func"/> evaluates to <c>false</c>
        /// optionally followed by indexes that evalates to <c>true</c>. The range may contain some indexes that evaluate to <c>null</c>.
        /// </summary>
        /// <returns>The first index that evaluate to <c>true</c>. Returns <c>null</c> if no such index is found.</returns>
        public static int BinaryFindFirst(Func<int, bool?> func, int first, int length)
        {
            // If the last item does not fit the criteria then don't bother looking any further.
            bool? res = (length >= 1) ? func(first + length - 1) : false;
            if (res == false)
                return -1;

            // If there is only one item left then it determines the outcome.
            if (length == 1)
                return (res == true) ? first : -1;

            // Otherwise split the array in two and search each half.
            int pivot = length / 2;
            var result = BinaryFindFirst(func, first, pivot);
            if (result == -1)
                return BinaryFindFirst(func, first + pivot, length - pivot);
            return result;
        }

        /// <summary>
        /// Finds the first element in an array which evaluates to <c>true</c> when <paramref name="func"/> is applied to it.
        /// The array should strictly contain zero or more elements for which <paramref name="func"/> evaluates to <c>false</c>
        /// optionally followed by elements that evalates to <c>true</c>. The array may contain some elements that evaluate to <c>null</c>.
        /// </summary>
        /// <returns>The first element that evaluate to <c>true</c>. Returns <c>null</c> if no such element is found.</returns>
        public static T BinaryFindFirst<T>(T[] array, Func<T, bool?> func)
        {
            Guard.Assert(default(T) == null);

            int pos = BinaryFindFirst(index => func(array[index]), 0, array.Length);
            if (pos < 0)
                return default(T);

            return array[pos];
        }
    }
}
