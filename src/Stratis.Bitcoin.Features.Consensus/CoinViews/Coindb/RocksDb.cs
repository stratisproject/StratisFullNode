using System;
using System.Collections.Generic;
using System.Linq;
using RocksDbSharp;
using NBitcoin;

namespace Stratis.Bitcoin.Features.Consensus.CoinViews
{
    public class RocksDbBatch : WriteBatch, IDbBatch
    {
        RocksDbSharp.RocksDb db;

        public RocksDbBatch(RocksDbSharp.RocksDb db)
        {
            this.db = db;
        }

        public IDbBatch Put(byte table, byte[] key, byte[] value)
        {
            return (IDbBatch)Put(new[] { table }.Concat(key).ToArray(), value);
        }

        public IDbBatch Delete(byte table, byte[] key)
        {
            return (IDbBatch)Delete(new[] { table }.Concat(key).ToArray());
        }

        public void Write()
        {
            this.db.Write(this);
        }
    }

    public class RocksDb : IDb
    {
        private static ByteArrayComparer byteArrayComparer = new ByteArrayComparer();

        private string name;

        RocksDbSharp.RocksDb db;

        public RocksDb()
        {
        }

        public void Open(string name)
        {
            this.name = name;
            this.db = RocksDbSharp.RocksDb.Open(new DbOptions().SetCreateIfMissing(), name);
        }

        public void Clear()
        {
            this.db.Dispose();
            System.IO.Directory.Delete(this.name, true);
            this.db = RocksDbSharp.RocksDb.Open(new DbOptions().SetCreateIfMissing(), this.name);
        }

        public IDbBatch GetWriteBatch() => new RocksDbBatch(this.db);

        public byte[] Get(byte table, byte[] key)
        {
            return this.db.Get(new[] { table }.Concat(key).ToArray());
        }

        public IEnumerable<(byte[], byte[])> GetAll(byte keyPrefix, bool keysOnly = false, bool ascending = true,
            byte[] firstKey = null, byte[] lastKey = null, bool includeFirstKey = true, bool includeLastKey = true)
        {
            using (Iterator iterator = this.db.NewIterator())
            {
                byte[] firstKeyBytes = (firstKey == null) ? null : new[] { keyPrefix }.Concat(firstKey).ToArray();
                byte[] lastKeyBytes = (lastKey == null) ? null : new[] { keyPrefix }.Concat(lastKey).ToArray();
                bool done = false;
                Func<byte[], bool> breakLoop;
                Action next;

                if (!ascending)
                {
                    if (lastKeyBytes == null)
                    {
                        // If no last key was provided then seek to the last record with this prefix
                        // by first seeking to the first record with the next prefix...
                        iterator.Seek(new[] { (byte)(keyPrefix + 1) });

                        // ...then back up to the previous value if the iterator is still valid.
                        if (iterator.Valid())
                            iterator.Prev();
                        else
                            // If the iterator is invalid then there were no records with greater prefixes.
                            // In this case we can simply seek to the last record.
                            iterator.SeekToLast();
                    }
                    else
                    {
                        // Seek to the last key if it was provided.
                        iterator.Seek(lastKeyBytes);

                        // If it won't be returned, and is current/found, then move to the previous value.
                        if (!iterator.Valid())
                            iterator.SeekToLast();
                        else if (!(includeLastKey && byteArrayComparer.Equals(iterator.Key(), lastKeyBytes)))
                            iterator.Prev();
                    }

                    breakLoop = (firstKeyBytes == null) ? (Func<byte[], bool>)null : (keyBytes) =>
                    {
                        int compareResult = byteArrayComparer.Compare(keyBytes, firstKeyBytes);
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
                    if (firstKeyBytes == null)
                    {
                        // If no first key was provided then use the key prefix to find the first value.
                        iterator.Seek(new[] { keyPrefix });
                    }
                    else
                    {
                        // Seek to the first key if it was provided.
                        iterator.Seek(firstKeyBytes);

                        // If it won't be returned, and is current/found, then move to the next value.
                        if (!includeFirstKey && iterator.Valid() && byteArrayComparer.Equals(iterator.Key(), firstKeyBytes))
                            iterator.Next();
                    }

                    breakLoop = (lastKeyBytes == null) ? (Func<byte[], bool>)null : (keyBytes) =>
                    {
                        int compareResult = byteArrayComparer.Compare(keyBytes, lastKeyBytes);
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

                while (iterator.Valid())
                {
                    byte[] keyBytes = iterator.Key();

                    if (keyBytes[0] != keyPrefix || (breakLoop != null && breakLoop(keyBytes)))
                        break;

                    yield return (keyBytes.Skip(1).ToArray(), keysOnly ? null : iterator.Value());

                    if (done)
                        break;

                    next();
                }
            }
        }

        public void Dispose()
        {
            this.db.Dispose();
        }
    }
}
