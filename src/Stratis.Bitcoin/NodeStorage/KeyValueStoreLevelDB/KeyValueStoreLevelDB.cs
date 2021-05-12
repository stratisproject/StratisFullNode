using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using LevelDB;
using Microsoft.Extensions.Logging;
using Stratis.Bitcoin.Interfaces;
using Stratis.Bitcoin.NodeStorage.KeyValueStore;
using Stratis.Bitcoin.Utilities;

namespace Stratis.Bitcoin.NodeStorage.KeyValueStoreLevelDB
{
    public class KeyValueStoreLevelDB : IKeyValueStoreRepository
    {
        internal class KeyValueStoreLDBTransaction : KeyValueStoreTransaction
        {
            public SnapShot Snapshot { get; private set; }

            public ReadOptions ReadOptions => (this.Snapshot == null) ? new ReadOptions() : new ReadOptions() { Snapshot = this.Snapshot };

            public KeyValueStoreLDBTransaction(KeyValueStoreLevelDB repository, KeyValueStoreTransactionMode mode, params string[] tables)
                : base(repository, mode, tables)
            {
                this.Snapshot = (mode == KeyValueStoreTransactionMode.Read) ? repository.Storage.CreateSnapshot() : null;
            }

            protected override void Dispose(bool disposing)
            {
                if (disposing)
                    this.Snapshot?.Dispose();

                base.Dispose(disposing);
            }
        }

        /// <summary>
        /// LevelDB does not understand the concept of tables. However this class introduces that concept in a way the LevelDB can understand.
        /// </summary>
        /// <remarks>
        /// The standard workaround is to prefix the key with the "table" identifier.
        /// </remarks>
        private class KeyValueStoreLDBTable : KeyValueStoreTable
        {
            public byte KeyPrefix { get; internal set; }
        }

        internal DB Storage { get; private set; }

        private int nextTablePrefix;
        private readonly SingleThreadResource transactionLock;
        private readonly ByteArrayComparer byteArrayComparer;

        public KeyValueStoreLevelDB(string rootPath, ILoggerFactory loggerFactory,
            IRepositorySerializer repositorySerializer)
        {
            var logger = loggerFactory.CreateLogger(nameof(KeyValueStoreLevelDB));

            this.transactionLock = new SingleThreadResource($"{nameof(this.transactionLock)}", logger);
            this.byteArrayComparer = new ByteArrayComparer();
            this.RepositorySerializer = repositorySerializer;
            this.Tables = new Dictionary<string, KeyValueStoreTable>();
            this.Init(rootPath);
        }

        public IRepositorySerializer RepositorySerializer { get; }

        public Dictionary<string, KeyValueStoreTable> Tables { get; }

        /// <summary>
        /// Initialize the underlying database / glue-layer.
        /// </summary>
        /// <param name="rootPath">The location of the key-value store.</param>
        private void Init(string rootPath)
        {
            var options = new Options()
            {
                CreateIfMissing = true,
            };

            this.Close();

            Directory.CreateDirectory(rootPath);

            try
            {
                this.Storage = new DB(options, rootPath);
            }
            catch (Exception err)
            {
                throw new Exception($"An error occurred while attempting to open the LevelDB database at '{rootPath}': {err.Message}'", err);
            }

            Guard.NotNull(this.Storage, nameof(this.Storage));

            this.Tables.Clear();
            for (this.nextTablePrefix = 1; ; this.nextTablePrefix++)
            {
                byte[] tableNameBytes = this.Storage.Get(new byte[] { 0, (byte)this.nextTablePrefix });
                if (tableNameBytes == null)
                    break;

                if (this.nextTablePrefix == 0xff)
                    throw new Exception($"Too many tables");

                string tableName = Encoding.ASCII.GetString(tableNameBytes);
                this.Tables[tableName] = new KeyValueStoreLDBTable()
                {
                    Repository = this,
                    TableName = tableName,
                    KeyPrefix = (byte)this.nextTablePrefix
                };
            }
        }

        public int Count(KeyValueStoreTransaction tran, KeyValueStoreTable table)
        {
            using (Iterator iterator = this.Storage.CreateIterator(((KeyValueStoreLDBTransaction)tran).ReadOptions))
            {
                int count = 0;

                byte keyPrefix = ((KeyValueStoreLDBTable)table).KeyPrefix;

                iterator.Seek(new[] { keyPrefix });

                while (iterator.IsValid())
                {
                    byte[] keyBytes = iterator.Key();

                    if (keyBytes[0] != keyPrefix)
                        break;

                    count++;

                    iterator.Next();
                }

                return count;
            }
        }

        public bool[] Exists(KeyValueStoreTransaction tran, KeyValueStoreTable table, byte[][] keys)
        {
            using (Iterator iterator = this.Storage.CreateIterator(((KeyValueStoreLDBTransaction)tran).ReadOptions))
            {
                byte keyPrefix = ((KeyValueStoreLDBTable)table).KeyPrefix;

                bool Exist(byte[] key)
                {
                    var keyBytes = new byte[] { keyPrefix }.Concat(key).ToArray();
                    iterator.Seek(keyBytes);
                    return iterator.IsValid() && this.byteArrayComparer.Equals(iterator.Key(), keyBytes);
                }

                (byte[] k, int n)[] orderedKeys = keys.Select((k, n) => (k, n)).OrderBy(t => t.k, this.byteArrayComparer).ToArray();
                var exists = new bool[keys.Length];
                for (int i = 0; i < orderedKeys.Length; i++)
                    exists[orderedKeys[i].n] = Exist(orderedKeys[i].k);

                return exists;
            }
        }

        public byte[][] Get(KeyValueStoreTransaction tran, KeyValueStoreTable table, byte[][] keys)
        {
            var keyBytes = keys.Select(key => new byte[] { ((KeyValueStoreLDBTable)table).KeyPrefix }.Concat(key).ToArray()).ToArray();
            (byte[] k, int n)[] orderedKeys = keyBytes.Select((k, n) => (k, n)).OrderBy(t => t.k, new ByteArrayComparer()).ToArray();
            var res = new byte[keys.Length][];
            for (int i = 0; i < orderedKeys.Length; i++)
            {
                if (orderedKeys[i].k == null)
                    continue;

                res[orderedKeys[i].n] = this.Storage.Get(orderedKeys[i].k, ((KeyValueStoreLDBTransaction)tran).ReadOptions);
            }

            return res;
        }

        public IEnumerable<(byte[], byte[])> GetAll(KeyValueStoreTransaction tran, KeyValueStoreTable table, bool keysOnly = false, SortOrder sortOrder = SortOrder.Ascending,
            byte[] firstKey = null, byte[] lastKey = null, bool includeFirstKey = true, bool includeLastKey = true)
        {
            using (Iterator iterator = this.Storage.CreateIterator(((KeyValueStoreLDBTransaction)tran).ReadOptions))
            {
                byte keyPrefix = ((KeyValueStoreLDBTable)table).KeyPrefix;
                byte[] firstKeyBytes = (firstKey == null) ? null : new[] { keyPrefix }.Concat(firstKey).ToArray();
                byte[] lastKeyBytes = (lastKey == null) ? null : new[] { keyPrefix }.Concat(lastKey).ToArray();
                bool done = false;
                Func<byte[], bool> breakLoop;
                Action next;

                if (sortOrder == SortOrder.Descending)
                {
                    if (lastKeyBytes == null)
                    {
                        // If no last key was provided then seek to the last record with this prefix
                        // by first seeking to the first record with the next prefix...
                        iterator.Seek(new[] { (byte)(keyPrefix + 1) });

                        // ...then back up to the previous value if the iterator is still valid.
                        if (iterator.IsValid())
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
                        if (!includeLastKey && iterator.IsValid() && this.byteArrayComparer.Equals(iterator.Key(), lastKeyBytes))
                            iterator.Prev();
                    }

                    breakLoop = (firstKeyBytes == null) ? (Func<byte[], bool>)null : (keyBytes) =>
                    {
                        int compareResult = this.byteArrayComparer.Compare(keyBytes, firstKeyBytes);
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
                        if (!includeFirstKey && iterator.IsValid() && this.byteArrayComparer.Equals(iterator.Key(), firstKeyBytes))
                            iterator.Next();
                    }

                    breakLoop = (lastKeyBytes == null) ? (Func<byte[], bool>)null : (keyBytes) =>
                    {
                        int compareResult = this.byteArrayComparer.Compare(keyBytes, lastKeyBytes);
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

                    if (keyBytes[0] != keyPrefix || (breakLoop != null && breakLoop(keyBytes)))
                        break;

                    yield return (keyBytes.Skip(1).ToArray(), keysOnly ? null : iterator.Value());

                    if (done)
                        break;

                    next();
                }
            }
        }

        public KeyValueStoreTable GetTable(string tableName)
        {
            if (!this.Tables.TryGetValue(tableName, out KeyValueStoreTable table))
            {
                if (this.nextTablePrefix >= 0xfe)
                    throw new Exception($"Too many tables");

                table = new KeyValueStoreLDBTable()
                {
                    Repository = this,
                    TableName = tableName,
                    KeyPrefix = (byte)this.nextTablePrefix++
                };

                this.Storage.Put(new byte[] { 0, ((KeyValueStoreLDBTable)table).KeyPrefix }, Encoding.ASCII.GetBytes(table.TableName));

                this.Tables[tableName] = table;
            }

            return table;
        }

        public KeyValueStoreTransaction CreateKeyValueStoreTransaction(KeyValueStoreTransactionMode mode, params string[] tables)
        {
            return new KeyValueStoreLDBTransaction(this, mode, tables);
        }

        public void OnBeginTransaction(KeyValueStoreTransaction keyValueStoreTransaction, KeyValueStoreTransactionMode mode)
        {
            if (mode == KeyValueStoreTransactionMode.ReadWrite)
            {
                this.transactionLock.Wait();
            }
        }

        public void OnCommit(KeyValueStoreTransaction keyValueStoreTransaction)
        {
            try
            {
                var writeBatch = new WriteBatch();
                var tableUpdates = ((KeyValueStoreLDBTransaction)keyValueStoreTransaction).TableUpdates;

                foreach (string tableName in ((KeyValueStoreLDBTransaction)keyValueStoreTransaction).TablesCleared)
                {
                    var table = (KeyValueStoreLDBTable)this.GetTable(tableName);
                    tableUpdates.TryGetValue(tableName, out ConcurrentDictionary<byte[], byte[]> tableUpdate);

                    foreach ((byte[] Key, byte[] _) kv in this.GetAll(keyValueStoreTransaction, table, true))
                    {
                        if (tableUpdate != null && tableUpdate.ContainsKey(kv.Key))
                            continue;

                        writeBatch.Delete(new byte[] { table.KeyPrefix }.Concat(kv.Key).ToArray());
                    }
                }

                foreach (KeyValuePair<string, ConcurrentDictionary<byte[], byte[]>> tableUpdate in tableUpdates)
                {
                    var table = (KeyValueStoreLDBTable)this.GetTable(tableUpdate.Key);

                    foreach (KeyValuePair<byte[], byte[]> kv in tableUpdate.Value)
                    {
                        if (kv.Value == null)
                        {
                            writeBatch.Delete(new byte[] { table.KeyPrefix }.Concat(kv.Key).ToArray());
                        }
                        else
                        {
                            writeBatch.Put(new byte[] { table.KeyPrefix }.Concat(kv.Key).ToArray(), kv.Value);
                        }
                    }
                }

                this.Storage.Write(writeBatch, new WriteOptions() { Sync = true });
            }
            finally
            {
                this.transactionLock.Release();
            }
        }

        public void OnRollback(KeyValueStoreTransaction keyValueStoreTransaction)
        {
            this.transactionLock.Release();
        }

        public void Close()
        {
            this.Storage?.Dispose();
            this.Storage = null;
        }

        public string[] GetTables()
        {
            return this.Tables.Select(t => t.Value.TableName).ToArray();
        }

        public IKeyValueStoreTransaction CreateTransaction(KeyValueStoreTransactionMode mode, params string[] tables)
        {
            return this.CreateKeyValueStoreTransaction(mode, tables);
        }

        // Public implementation of Dispose pattern callable by consumers.
        public void Dispose()
        {
            this.Dispose(true);
            GC.SuppressFinalize(this);
        }

        /// <summary>Protected implementation of Dispose pattern.</summary>
        /// <param name="disposing">Indicates whether disposing.</param>
        protected virtual void Dispose(bool disposing)
        {
            if (disposing)
                this.Close();
        }

        public byte[] Serialize<T>(T obj)
        {
            return this.RepositorySerializer.Serialize(obj);
        }

        public T Deserialize<T>(byte[] objBytes)
        {
            return this.RepositorySerializer.Deserialize<T>(objBytes);
        }
    }
}
