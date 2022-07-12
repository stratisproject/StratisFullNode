using System.Linq;
using LevelDB;
using NBitcoin;

namespace Stratis.Bitcoin.Database
{
    /// <summary>A minimal LevelDb wrapper that makes it compliant with the <see cref="IDb"/> interface.</summary>
    public class LevelDb : IDb
    {
        private string dbPath;

        private DB db;

        public IDbIterator GetIterator(byte table)
        {
            return new LevelDbIterator(table, this.db.CreateIterator());
        }

        public void Open(string dbPath)
        {
            this.dbPath = dbPath;
            this.db = new DB(new Options() { CreateIfMissing = true }, dbPath);
        }

        public void Clear()
        {
            this.db.Dispose();
            System.IO.Directory.Delete(this.dbPath, true);
            this.db = new DB(new Options() { CreateIfMissing = true }, this.dbPath);
        }

        public IDbBatch GetWriteBatch() => new LevelDbBatch(this.db);

        public byte[] Get(byte table, byte[] key)
        {
            return this.db.Get(new[] { table }.Concat(key).ToArray());
        }

        public void Dispose()
        {
            this.db.Dispose();
        }
    }

    /// <summary>A minimal LevelDb wrapper that makes it compliant with the <see cref="IDbBatch"/> interface.</summary>
    public class LevelDbBatch : WriteBatch, IDbBatch
    {
        private DB db;

        public LevelDbBatch(DB db)
        {
            this.db = db;
        }

        public IDbBatch Put(byte table, byte[] key, byte[] value)
        {
            return (IDbBatch)this.Put(new[] { table }.Concat(key).ToArray(), value);
        }

        public IDbBatch Delete(byte table, byte[] key)
        {
            return (IDbBatch)this.Delete(new[] { table }.Concat(key).ToArray());
        }

        public void Write()
        {
            this.db.Write(this, new WriteOptions() { Sync = true });
        }
    }

    /// <summary>A minimal LevelDb wrapper that makes it compliant with the <see cref="IDbIterator"/> interface.</summary>
    public class LevelDbIterator : IDbIterator
    {
        private byte table;
        private Iterator iterator;

        public LevelDbIterator(byte table, Iterator iterator)
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
            // First seek past the last record in the table by attempting to seek to the start of the next table (if any).
            this.iterator.Seek(new[] { (byte)(this.table + 1) });
            if (!this.iterator.IsValid())
                // If there is no next table then simply seek to the last record in the db as that will be the last record of 'table'.
                this.iterator.SeekToLast();
            else
                // If we managed to seek to the start of the next table then go back one record to arrive at the last record of 'table'.
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
            return this.iterator.IsValid() && this.iterator.Key()[0] == this.table;
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
