using System;
using System.Collections.Generic;
using System.Linq;
using NBitcoin;

namespace Stratis.Bitcoin.Features.Consensus.CoinViews
{
    public interface IDb : IDisposable
    {
        void Open(string name);

        byte[] Get(byte table, byte[] key);

        IDbIterator GetIterator(byte table);

        IDbBatch GetWriteBatch();

        void Clear();
    }

    public interface IDbBatch : IDisposable
    {
        IDbBatch Put(byte table, byte[] key, byte[] value);

        IDbBatch Delete(byte table, byte[] key);

        void Write();
    }

    public class ReadWriteBatch : IDbBatch
    {
        private readonly IDb db;
        private readonly IDbBatch batch;
        private Dictionary<byte[], byte[]> cache;

        public ReadWriteBatch(IDb db)
        {
            this.db = db;
            this.batch = db.GetWriteBatch();
            this.cache = new Dictionary<byte[], byte[]>(new ByteArrayComparer());
        }

        public IDbBatch Put(byte table, byte[] key, byte[] value)
        {
            this.cache[new byte[] { table }.Concat(key).ToArray()] = value;
            return this.batch.Put(table, key, value);
        }

        public IDbBatch Delete(byte table, byte[] key)
        {
            this.cache[new byte[] { table }.Concat(key).ToArray()] = null;
            return this.batch.Delete(table, key);
        }

        public byte[] Get(byte table, byte[] key)
        {
            if (this.cache.TryGetValue(new byte[] { table }.Concat(key).ToArray(), out byte[] value))
                return value;

            return this.db.Get(table, key);
        }

        public void Write()
        {
            this.batch.Write();
        }

        public void Dispose()
        {
            this.batch.Dispose();
        }
    }

    public static class IDbExt
    {
        public static ReadWriteBatch GetReadWriteBatch(this IDb db)
        {
            return new ReadWriteBatch(db);
        }
    }

    public interface IDbIterator : IDisposable
    {
        void Seek(byte[] key);
        void SeekToLast();
        void Next();
        void Prev();
        bool IsValid();
        byte[] Key();
        byte[] Value();
    }

    public static class IDbIteratorExt
    {
        private static ByteArrayComparer byteArrayComparer = new ByteArrayComparer();

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
                    if (!(includeLastKey && iterator.IsValid() && byteArrayComparer.Equals(iterator.Key(), lastKey)))
                        iterator.Prev();
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
                    if (!(includeFirstKey && iterator.IsValid() && byteArrayComparer.Equals(iterator.Key(), firstKey)))
                        iterator.Next();
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
