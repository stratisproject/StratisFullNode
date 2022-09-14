using System.Linq;
using LevelDB;

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

        public IDbIterator GetIterator()
        {
            return new LevelDbIterator(this.db.CreateIterator());
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

        public IDbBatch GetWriteBatch(params byte[] tables) => new LevelDbBatch(this.db);

        public byte[] Get(byte table, byte[] key)
        {
            return this.db.Get(new[] { table }.Concat(key).ToArray());
        }

        public byte[] Get(byte[] key)
        {
            return this.db.Get(key);
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

        // Methods when using tables.

        public IDbBatch Put(byte table, byte[] key, byte[] value)
        {
            return this.Put(new[] { table }.Concat(key).ToArray(), value);
        }

        public IDbBatch Delete(byte table, byte[] key)
        {
            return this.Delete(new[] { table }.Concat(key).ToArray());
        }

        // Table-less operations.

        public new IDbBatch Put(byte[] key, byte[] value)
        {
            base.Put(key, value);
            return this;
        }

        public new IDbBatch Delete(byte[] key)
        {
            base.Delete(key);
            return this;
        }

        public void Write()
        {
            this.db.Write(this, new WriteOptions() { Sync = true });
        }
    }

    /// <summary>A minimal LevelDb wrapper that makes it compliant with the <see cref="IDbIterator"/> interface.</summary>
    public class LevelDbIterator : IDbIterator
    {
        private byte? table;
        private Iterator iterator;

        public LevelDbIterator(byte table, Iterator iterator)
        {
            this.table = table;
            this.iterator = iterator;
        }

        // Table-less constructor.
        public LevelDbIterator(Iterator iterator)
        {
            this.iterator = iterator;
        }

        public void Seek(byte[] key)
        {
            this.iterator.Seek(this.table.HasValue ? (new[] { this.table.Value }.Concat(key).ToArray()) : key);
        }

        public void SeekToLast()
        {
            if (!this.table.HasValue)
            {
                this.iterator.SeekToLast();
                return;
            }

            if (this.table != 255)
            {
                // First seek past the last record in the table by attempting to seek to the start of the next table (if any).
                this.iterator.Seek(new[] { (byte)(this.table + 1) });

                // If we managed to seek to the start of the next table then go back one record to arrive at the last record of 'table'.
                if (this.iterator.IsValid())
                {
                    this.iterator.Prev();
                    return;
                }
            }

            // If there is no next table then simply seek to the last record in the db as that will be the last record of 'table'.
            this.iterator.SeekToLast();
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
            return this.iterator.IsValid() && (!this.table.HasValue || this.iterator.Key()[0] == this.table);
        }

        public byte[] Key()
        {
            return this.table.HasValue ? this.iterator.Key().Skip(1).ToArray() : this.iterator.Key();
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
