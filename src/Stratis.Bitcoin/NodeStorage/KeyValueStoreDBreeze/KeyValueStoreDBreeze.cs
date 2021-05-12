using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using DBreeze;
using DBreeze.Utils;
using Microsoft.Extensions.Logging;
using Stratis.Bitcoin.Interfaces;
using Stratis.Bitcoin.NodeStorage.KeyValueStore;
using Stratis.Bitcoin.Utilities;

namespace Stratis.Bitcoin.NodeStorage.KeyValueStoreDBreeze
{
    public class KeyValueStoreDBreeze : IKeyValueStoreRepository
    {
        internal class KeyValueStoreDBZTransaction : KeyValueStoreTransaction
        {
            public DBreeze.Transactions.Transaction DBreezeTransaction { get; private set; }

            public KeyValueStoreDBZTransaction(KeyValueStoreDBreeze repository, KeyValueStoreTransactionMode mode, params string[] tables)
                : base(repository, mode, tables)
            {
                this.DBreezeTransaction = repository.storage.GetTransaction();
                if (mode == KeyValueStoreTransactionMode.Read && tables.Length > 0)
                    this.DBreezeTransaction.SynchronizeTables(tables);
            }
        }

        private DBreezeEngine storage;
        private SingleThreadResource transactionLock;

        public KeyValueStoreDBreeze(string rootPath, ILoggerFactory loggerFactory, IRepositorySerializer repositorySerializer)
        {
            var logger = loggerFactory.CreateLogger(nameof(KeyValueStoreLevelDB));

            this.transactionLock = new SingleThreadResource($"{nameof(this.transactionLock)}", logger);
            this.RepositorySerializer = repositorySerializer;
            this.Tables = new Dictionary<string, KeyValueStoreTable>();
            this.Init(rootPath);
        }

        public byte[] Serialize<T>(T obj)
        {
            if (typeof(T).IsValueType)
                return ((T)obj).ToBytes();

            return this.RepositorySerializer.Serialize(obj);
        }

        public T Deserialize<T>(byte[] objBytes)
        {
            if (typeof(T).IsValueType)
                return (T)(object)DBreeze.DataTypes.DataTypesConvertor.ConvertBack<T>(objBytes);

            return this.RepositorySerializer.Deserialize<T>(objBytes);
        }

        public IRepositorySerializer RepositorySerializer { get; }

        public Dictionary<string, KeyValueStoreTable> Tables { get; }

        private void Init(string rootPath)
        {
            this.Close();
            this.storage = new DBreezeEngine(rootPath);

            // Enumerate the tables.
            this.Tables.Clear();

            foreach (string tableName in this.storage.Scheme.GetUserTableNamesStartingWith(string.Empty))
            {
                this.Tables.Add(tableName, new KeyValueStoreTable() { TableName = tableName, Repository = this });
            }
        }

        public int Count(KeyValueStoreTransaction keyValueStoreTransaction, KeyValueStoreTable table)
        {
            var tran = (KeyValueStoreDBZTransaction)keyValueStoreTransaction;
            var dbTransaction = tran.DBreezeTransaction;

            return (int)dbTransaction.Count(table.TableName);
        }

        public bool[] Exists(KeyValueStoreTransaction keyValueStoreTransaction, KeyValueStoreTable table, byte[][] keys)
        {
            var tran = (KeyValueStoreDBZTransaction)keyValueStoreTransaction;
            var dbTransaction = tran.DBreezeTransaction;

            dbTransaction.ValuesLazyLoadingIsOn = true;
            try
            {
                (byte[] k, int n)[] orderedKeys = keys.Select((k, n) => (k, n)).OrderBy(t => t.k, new ByteListComparer()).ToArray();

                var exists = new bool[keys.Length];
                for (int i = 0; i < orderedKeys.Length; i++)
                    exists[orderedKeys[i].n] = dbTransaction.Select<byte[], byte[]>(table.TableName, orderedKeys[i].k).Exists;

                return exists;
            }
            finally
            {
                dbTransaction.ValuesLazyLoadingIsOn = false;
            }
        }

        public byte[][] Get(KeyValueStoreTransaction keyValueStoreTransaction, KeyValueStoreTable table, byte[][] keys)
        {
            var tran = (KeyValueStoreDBZTransaction)keyValueStoreTransaction;
            var dbTransaction = tran.DBreezeTransaction;

            (byte[] k, int n)[] orderedKeys = keys.Select((k, n) => (k, n)).OrderBy(t => t.k, new ByteListComparer()).ToArray();
            var res = new byte[keys.Length][];
            for (int i = 0; i < orderedKeys.Length; i++)
            {
                var key = orderedKeys[i].k;
                if (key != null)
                {
                    var row = dbTransaction.Select<byte[], byte[]>(table.TableName, key);
                    res[orderedKeys[i].n] = row.Exists ? row.Value : null;
                }
            }

            return res;
        }

        public IEnumerable<(byte[], byte[])> GetAll(KeyValueStoreTransaction keyValueStoreTransaction, KeyValueStoreTable table, bool keysOnly = false, SortOrder sortOrder = SortOrder.Ascending,
            byte[] firstKey = null, byte[] lastKey = null, bool includeFirstKey = true, bool includeLastKey = true)
        {
            if (firstKey != null || lastKey != null || !includeFirstKey || !includeLastKey)
                throw new NotSupportedException();

            var tran = (KeyValueStoreDBZTransaction)keyValueStoreTransaction;
            var dbTransaction = tran.DBreezeTransaction;

            dbTransaction.ValuesLazyLoadingIsOn = keysOnly;

            try
            {
                if (sortOrder == SortOrder.Descending)
                {
                    foreach (var row in dbTransaction.SelectBackward<byte[], byte[]>(table.TableName))
                    {
                        yield return (row.Key, keysOnly ? null : row.Value);
                    }
                }
                else
                {
                    foreach (var row in dbTransaction.SelectForward<byte[], byte[]>(table.TableName))
                    {
                        yield return (row.Key, keysOnly ? null : row.Value);
                    }
                }
            }
            finally
            {
                dbTransaction.ValuesLazyLoadingIsOn = false;
            }
        }

        public KeyValueStoreTable GetTable(string tableName)
        {
            if (!this.Tables.TryGetValue(tableName, out KeyValueStoreTable table))
            {
                table = new KeyValueStoreTable()
                {
                    Repository = this,
                    TableName = tableName
                };

                this.Tables[tableName] = table;
            }

            return table;
        }

        public KeyValueStoreTransaction CreateKeyValueStoreTransaction(KeyValueStoreTransactionMode mode, params string[] tables)
        {
            return new KeyValueStoreDBZTransaction(this, mode, tables);
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
            var tran = (KeyValueStoreDBZTransaction)keyValueStoreTransaction;
            var dbTransaction = tran.DBreezeTransaction;

            var tablesModified = tran.TablesCleared.Concat(tran.TableUpdates.Keys).Distinct().ToArray();
            if (tablesModified.Length > 0)
                dbTransaction.SynchronizeTables(tablesModified);

            try
            {
                foreach (string tableName in tran.TablesCleared)
                {
                    var table = this.GetTable(tableName);

                    dbTransaction.RemoveAllKeys(tableName, true);
                }

                foreach (KeyValuePair<string, ConcurrentDictionary<byte[], byte[]>> updates in tran.TableUpdates)
                {
                    var table = this.GetTable(updates.Key);

                    foreach (KeyValuePair<byte[], byte[]> kv in updates.Value)
                    {
                        if (kv.Value == null)
                        {
                            dbTransaction.RemoveKey(updates.Key, kv.Key);
                        }
                        else
                        {
                            dbTransaction.Insert(updates.Key, kv.Key, kv.Value);
                        }
                    }
                }
            }
            finally
            {
                dbTransaction.Commit();
                dbTransaction.Dispose();

                this.transactionLock.Release();
            }
        }

        public void OnRollback(KeyValueStoreTransaction keyValueStoreTransaction)
        {
            var tran = (KeyValueStoreDBZTransaction)keyValueStoreTransaction;
            var dbTransaction = tran.DBreezeTransaction;

            dbTransaction.Rollback();
            dbTransaction.Dispose();

            this.transactionLock.Release();
        }

        public void Close()
        {
            this.storage?.Dispose();
            this.storage = null;
        }

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

        public string[] GetTables()
        {
            return this.Tables.Select(t => t.Value.TableName).ToArray();
        }

        public IKeyValueStoreTransaction CreateTransaction(KeyValueStoreTransactionMode mode, params string[] tables)
        {
            return this.CreateKeyValueStoreTransaction(mode, tables);
        }
    }
}
