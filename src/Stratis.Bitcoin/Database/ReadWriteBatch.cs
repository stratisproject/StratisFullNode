using System.Collections.Generic;
using System.Linq;
using NBitcoin;

namespace Stratis.Bitcoin.Database
{
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

        public ReadWriteBatch(IDb db, params byte[] tables)
        {
            this.db = db;
            this.batch = db.GetWriteBatch(tables);
            this.cache = new Dictionary<byte[], byte[]>(new ByteArrayComparer());
        }

        /// <summary>
        /// Records a value that will be written to the database when the <see cref="Write"/> method is invoked.
        /// </summary>
        /// <param name="key">The table key that identifies the value to be updated.</param>
        /// <param name="value">The value to be written to the table.</param>
        /// <returns>This class for fluent operations.</returns>
        public IDbBatch Put(byte[] key, byte[] value)
        {
            this.cache[key] = value;
            return this.batch.Put(key, value);
        }

        /// <summary>
        /// Records a value that will be written to the database when the <see cref="Write"/> method is invoked.
        /// </summary>
        /// <param name="table">The table that will be updated.</param>
        /// <param name="key">The table key that identifies the value to be updated.</param>
        /// <param name="value">The value to be written to the table.</param>
        /// <returns>This interface for fluent operations.</returns>
        public IDbBatch Put(byte table, byte[] key, byte[] value)
        {
            this.cache[new byte[] { table }.Concat(key).ToArray()] = value;
            return this.batch.Put(table, key, value);
        }

        /// <summary>
        /// Records a key that will be deleted from the database when the <see cref="Write"/> method is invoked.
        /// </summary>
        /// <param name="key">The table key that will be removed.</param>
        /// <returns>This interface for fluent operations.</returns>
        public IDbBatch Delete(byte[] key)
        {
            this.cache[key] = null;
            return this.batch.Delete(key);
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
        /// <param name="key">The table key of the value to retrieve.</param>
        /// <returns>This interface for fluent operations.</returns>
        public byte[] Get(byte[] key)
        {
            if (this.cache.TryGetValue(key, out byte[] value))
                return value;

            return this.db.Get(key);
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
        public static ReadWriteBatch GetReadWriteBatch(this IDb db, params byte[] tables)
        {
            return new ReadWriteBatch(db, tables);
        }
    }
}