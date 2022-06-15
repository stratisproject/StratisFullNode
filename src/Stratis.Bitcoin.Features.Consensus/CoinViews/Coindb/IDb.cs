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

        IEnumerable<(byte[], byte[])> GetAll(byte table, bool keysOnly = false, bool ascending = true,
            byte[] firstKey = null, byte[] lastKey = null, bool includeFirstKey = true, bool includeLastKey = true);

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
}
