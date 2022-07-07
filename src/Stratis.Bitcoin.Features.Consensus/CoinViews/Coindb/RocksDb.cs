using System.Linq;
using RocksDbSharp;
using NBitcoin;

namespace Stratis.Bitcoin.Features.Consensus.CoinViews
{
    /// <inheritdoc/>
    public class RocksDb : IDb
    {
        private string dbPath;

        RocksDbSharp.RocksDb db;

        public IDbIterator GetIterator(byte table)
        {
            return new RocksDbIterator(table, this.db.NewIterator());
        }

        public void Open(string dbPath)
        {
            this.dbPath = dbPath;
            this.db = RocksDbSharp.RocksDb.Open(new DbOptions().SetCreateIfMissing(), dbPath);
        }

        public void Clear()
        {
            this.db.Dispose();
            System.IO.Directory.Delete(this.dbPath, true);
            this.db = RocksDbSharp.RocksDb.Open(new DbOptions().SetCreateIfMissing(), this.dbPath);
        }

        public IDbBatch GetWriteBatch() => new RocksDbBatch(this.db);

        public byte[] Get(byte table, byte[] key)
        {
            return this.db.Get(new[] { table }.Concat(key).ToArray());
        }

        public void Dispose()
        {
            this.db.Dispose();
        }
    }

    /// <inheritdoc/>
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

    /// <inheritdoc/>
    public class RocksDbIterator : IDbIterator
    {
        byte table;
        Iterator iterator;

        public RocksDbIterator(byte table, Iterator iterator)
        {
            this.table = table;
            this.iterator = iterator;
        }

        public void Seek(byte[] key)
        {
            this.iterator.Seek(new[] { this.table }.Concat(key).ToArray());
        }

        public void SeekToLast()
        {
            this.iterator.Seek(new[] { (byte)(this.table + 1) });
            if (!this.iterator.Valid())
                this.iterator.SeekToLast();
            else
                this.iterator.Prev();
        }

        public void Next()
        {
            this.iterator.Next();
        }

        public void Prev()
        {
            this.iterator.Prev();
        }

        public bool IsValid()
        {
            return this.iterator.Valid() && this.iterator.Value()[0] == this.table;
        }

        public byte[] Key()
        {
            return this.iterator.Key().Skip(1).ToArray();
        }

        public byte[] Value()
        {
            return this.iterator.Value();
        }

        public void Dispose()
        {
            this.iterator.Dispose();
        }
    }
}
