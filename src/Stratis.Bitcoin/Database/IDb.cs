using System;
using System.Collections.Generic;
using System.Linq;
using NBitcoin;

namespace Stratis.Bitcoin.Database
{
    /// <summary>
    /// This interface and its relevant implementations provides a common way to interact with <see cref="RocksDb"/> and <see cref="LevelDb"/> databases.
    /// </summary>
    public interface IDb : IDisposable
    {
        /// <summary>
        /// Opens the database at the specified path.
        /// </summary>
        /// <param name="dbPath">The path where the database is located.</param>
        void Open(string dbPath);

        /// <summary>
        /// Gets the value associated with a table and key.
        /// </summary>
        /// <param name="table">The table identifier.</param>
        /// <param name="key">The key of the value to retrieve.</param>
        /// <returns>The value for the specified table and key.</returns>
        byte[] Get(byte table, byte[] key);

        /// <summary>
        /// Gets an iterator that allows iteration over keys in a table.
        /// </summary>
        /// <param name="table">The table that will be iterated.</param>
        /// <returns>See <see cref="IDbIterator"/>.</returns>
        IDbIterator GetIterator(byte table);

        /// <summary>
        /// Gets a batch that can be used to record changes that can be applied atomically.
        /// </summary>
        /// <remarks>The <see cref="Get"/> method will not reflect these changes until they are committed.</remarks>
        /// <returns>See <see cref="IDbBatch"/>.</returns>
        IDbBatch GetWriteBatch();

        /// <summary>
        /// Removes all tables and their contents.
        /// </summary>
        void Clear();
    }

    /// <summary>
    /// A batch that can be used to record changes that can be applied atomically.
    /// </summary>
    /// <remarks>The database's <see cref="Get"/> method will not reflect these changes until they are committed.</remarks>
    public interface IDbBatch : IDisposable
    {
        /// <summary>
        /// Records a value that will be written to the database when the <see cref="Write"/> method is invoked.
        /// </summary>
        /// <param name="table">The table that will be updated.</param>
        /// <param name="key">The table key that identifies the value to be updated.</param>
        /// <param name="value">The value to be written to the table.</param>
        /// <returns>This class for fluent operations.</returns>
        IDbBatch Put(byte table, byte[] key, byte[] value);

        /// <summary>
        /// Records a key that will be deleted from the database when the <see cref="Write"/> method is invoked.
        /// </summary>
        /// <param name="table">The table that will be updated.</param>
        /// <param name="key">The table key that will be removed.</param>
        /// <returns>This class for fluent operations.</returns>
        IDbBatch Delete(byte table, byte[] key);

        /// <summary>
        /// Writes the recorded changes to the database.
        /// </summary>
        void Write();
    }

    /// <summary>
    /// A batch that can be used to record changes that can be applied atomically.
    /// </summary>
    /// <remarks>The supplied <see cref="Get"/> method will immediately reflect any changes that have 
    /// been made or retrieve the value from the underlying database. In contrast the database <see cref="IDb.Get"/> method
    /// will only show the changes after the <see cref="Write"/> method is called.</remarks>
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

        /// <summary>
        /// Records a value that will be written to the database when the <see cref="Write"/> method is invoked.
        /// </summary>
        /// <param name="table">The table that will be updated.</param>
        /// <param name="key">The table key that identifies the value to be updated.</param>
        /// <param name="value">The value to be written to the table.</param>
        /// <returns>This class for fluent operations.</returns>
        public IDbBatch Put(byte table, byte[] key, byte[] value)
        {
            this.cache[new byte[] { table }.Concat(key).ToArray()] = value;
            return this.batch.Put(table, key, value);
        }

        /// <summary>
        /// Records a key that will be deleted from the database when the <see cref="Write"/> method is invoked.
        /// </summary>
        /// <param name="table">The table that will be updated.</param>
        /// <param name="key">The table key that will be removed.</param>
        /// <returns>This interface for fluent operations.</returns>
        public IDbBatch Delete(byte table, byte[] key)
        {
            this.cache[new byte[] { table }.Concat(key).ToArray()] = null;
            return this.batch.Delete(table, key);
        }

        /// <summary>
        /// Returns any changes that have been made to the batch or retrieves the value from the underlying database..
        /// </summary>
        /// <param name="table">The table of the value to be retrieved.</param>
        /// <param name="key">The table key of the value to retrieve.</param>
        /// <returns>This interface for fluent operations.</returns>
        public byte[] Get(byte table, byte[] key)
        {
            if (this.cache.TryGetValue(new byte[] { table }.Concat(key).ToArray(), out byte[] value))
                return value;

            return this.db.Get(table, key);
        }

        /// <summary>
        /// Writes the recorded changes to the database.
        /// </summary>
        public void Write()
        {
            this.batch.Write();
        }

        public void Dispose()
        {
            this.batch.Dispose();
        }
    }

    /// <summary>
    /// Extension methods that build on the <see cref="IDb"/> interface.
    /// </summary>
    public static class IDbExt
    {
        /// <summary>
        /// Gets a <see cref="ReadWriteBatch"/>.
        /// </summary>
        /// <param name="db">The database to get the batch for.</param>
        /// <returns>The <see cref="ReadWriteBatch"/>.</returns>
        public static ReadWriteBatch GetReadWriteBatch(this IDb db)
        {
            return new ReadWriteBatch(db);
        }
    }

    /// <summary>
    /// An iterator that can be used to iterate the keys and values in an <see cref="IDb"/> compliant database.
    /// </summary>
    public interface IDbIterator : IDisposable
    {
        /// <summary>
        /// Seeks to a first key >= <paramref name="key"/> in the relevant table.
        /// If no such key is found then <see cref="IsValid"/> will return <c>false</c>.
        /// </summary>
        /// <param name="key">The key to find.</param>
        void Seek(byte[] key);

        /// <summary>
        /// Seeks to the last key in the relevant table.
        /// If no such key is found then <see cref="IsValid"/> will return <c>false</c>.
        /// </summary>
        void SeekToLast();

        /// <summary>
        /// Seeks to the next key in the relevant table.
        /// If no such key is found then <see cref="IsValid"/> will return <c>false</c>.
        /// </summary>
        void Next();

        /// <summary>
        /// Seeks to the previous key in the relevant table.
        /// If no such key is found then <see cref="IsValid"/> will return <c>false</c>.
        /// </summary>
        void Prev();

        /// <summary>
        /// Determines if the current key is valid.
        /// </summary>
        /// <returns><c>true</c> if a <see cref="Seek"/>, <see cref="Next"/>, <see cref="SeekToLast"/> or <see cref="Prev"/> operation found a valid key. <c>false</c> otherwise.</returns>
        bool IsValid();

        /// <summary>
        /// The current key.
        /// </summary>
        /// <returns>The key.</returns>
        byte[] Key();

        /// <summary>
        /// The current value.
        /// </summary>
        /// <returns>The value.</returns>
        byte[] Value();
    }

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
