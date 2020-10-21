using System;

namespace Stratis.Bitcoin.Utilities
{
    public class BinarySearch
    {
        public static T BinaryFindFirst<T>(T[] array, Func<T, bool?> func, int first = 0, int? span = null)
        {
            int length = span ?? array.Length;

            if (length == 0)
                return default;

            // If the first item matches the criteria then take it and don't bother looking beyond it.
            if (func(array[first]) == true)
                return array[first];

            if (length <= 1)
                return default;

            // If the last item does not fit the criteria then don't bother looking any further.
            if (func(array[first + length - 1]) == false)
                return default;

            int pivot = 1 + (length - 1) / 2;

            return BinaryFindFirst(array, func, first + 1, pivot)
                ?? BinaryFindFirst(array, func, first + 1 + pivot, length - 1 - pivot);
        }
    }
}
