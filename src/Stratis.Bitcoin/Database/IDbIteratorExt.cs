using System;
using System.Collections.Generic;
using NBitcoin;

namespace Stratis.Bitcoin.Database
{
    /// <summary>
    /// Extension methods that build on the <see cref="IDbIterator"/> interface.
    /// </summary>
    public static class IDbIteratorExt
    {
        private static ByteArrayComparer byteArrayComparer = new ByteArrayComparer();

        /// <summary>
        /// Gets all the keys in the relevant table subject to any supplied constraints.
        /// </summary>
        /// <param name="iterator">The iterator that also identifies the table being iterated.</param>
        /// <param name="keysOnly">Defaults to <c>false</c>. Set to <c>true</c> if values should be ommitted - i.e. set to <c>null</c>.</param>
        /// <param name="ascending">Defaults to <c>true</c>. Set to <c>false</c> to return keys in ascending order.</param>
        /// <param name="firstKey">Can be set optionally to specify the lower bound of keys to return.</param>
        /// <param name="lastKey">Can be set optionally to specify the upper bound of keys to return.</param>
        /// <param name="includeFirstKey">Defaults to <c>true</c>. Set to <c>false</c> to omit the key specified in <paramref name="firstKey"/>.</param>
        /// <param name="includeLastKey">Defaults to <c>true</c>. Set to <c>false</c> to omit the key specified in <paramref name="lastKey"/>.</param>
        /// <returns>An enumeration containing all the keys and values according to the specified constraints.</returns>
        public static IEnumerable<(byte[], byte[])> GetAll(this IDbIterator iterator, bool keysOnly = false, bool ascending = true,
            byte[] firstKey = null, byte[] lastKey = null, bool includeFirstKey = true, bool includeLastKey = true)
        {
            bool done = false;
            Func<byte[], bool> breakLoop;
            Action next;

            if (!ascending)
            {
                // Seek to the last key if it was provided.
                if (lastKey == null)
                    iterator.SeekToLast();
                else
                {
                    iterator.Seek(lastKey);
                    if (iterator.IsValid())
                    {
                        if (!(includeLastKey && byteArrayComparer.Equals(iterator.Key(), lastKey)))
                            iterator.Prev();
                    }
                    else
                        iterator.SeekToLast();
                }

                breakLoop = (firstKey == null) ? (Func<byte[], bool>)null : (keyBytes) =>
                {
                    int compareResult = byteArrayComparer.Compare(keyBytes, firstKey);
                    if (compareResult <= 0)
                    {
                        // If this is the first key and its not included or we've overshot the range then stop without yielding a value.
                        if (!includeFirstKey || compareResult < 0)
                            return true;

                        // Stop after yielding the value.
                        done = true;
                    }

                    // Keep going.
                    return false;
                };

                next = () => iterator.Prev();
            }
            else /* Ascending */
            {
                // Seek to the first key if it was provided.
                if (firstKey == null)
                    iterator.Seek(new byte[0]);
                else
                {
                    iterator.Seek(firstKey);
                    if (iterator.IsValid())
                    {
                        if (!(includeFirstKey && byteArrayComparer.Equals(iterator.Key(), firstKey)))
                            iterator.Next();
                    }
                }

                breakLoop = (lastKey == null) ? (Func<byte[], bool>)null : (keyBytes) =>
                {
                    int compareResult = byteArrayComparer.Compare(keyBytes, lastKey);
                    if (compareResult >= 0)
                    {
                        // If this is the last key and its not included or we've overshot the range then stop without yielding a value.
                        if (!includeLastKey || compareResult > 0)
                            return true;

                        // Stop after yielding the value.
                        done = true;
                    }

                    // Keep going.
                    return false;
                };

                next = () => iterator.Next();
            }

            while (iterator.IsValid())
            {
                byte[] keyBytes = iterator.Key();

                if (breakLoop != null && breakLoop(keyBytes))
                    break;

                yield return (keyBytes, keysOnly ? null : iterator.Value());

                if (done)
                    break;

                next();
            }
        }
    }
}