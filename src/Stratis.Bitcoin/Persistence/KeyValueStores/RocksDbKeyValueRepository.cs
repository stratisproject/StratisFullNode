using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using RocksDbSharp;
using Stratis.Bitcoin.Configuration;
using Stratis.Bitcoin.Utilities;
using Stratis.Bitcoin.Utilities.JsonConverters;

namespace Stratis.Bitcoin.Persistence.KeyValueStores
{
    public class RocksDbKeyValueRepository : IKeyValueRepository
    {
        private readonly string dataFolder;
        private readonly DBreezeSerializer dataStoreSerializer;
        private readonly DbOptions dbOptions;
        private readonly RocksDb rocksdb;

        public RocksDbKeyValueRepository(DataFolder dataFolder, DBreezeSerializer dataStoreSerializer)
        {
            this.dataFolder = dataFolder.KeyValueRepositoryPath;
            Directory.CreateDirectory(this.dataFolder);
            this.dataStoreSerializer = dataStoreSerializer;
            this.dbOptions = new DbOptions().SetCreateIfMissing(true);
            this.rocksdb = RocksDb.Open(this.dbOptions, this.dataFolder);
        }

        /// <inheritdoc />
        public void SaveBytes(string key, byte[] bytes, bool overWrite = false)
        {
            byte[] keyBytes = Encoding.ASCII.GetBytes(key);

            if (overWrite)
            {
                byte[] row = this.rocksdb.Get(keyBytes);
                if (row != null)
                    this.rocksdb.Remove(keyBytes);
            }

            this.rocksdb.Put(keyBytes, bytes);
        }

        /// <inheritdoc />
        public void SaveValue<T>(string key, T value, bool overWrite = false)
        {
            this.SaveBytes(key, this.dataStoreSerializer.Serialize(value), overWrite);
        }

        /// <inheritdoc />
        public void SaveValueJson<T>(string key, T value, bool overWrite = false)
        {
            string json = Serializer.ToString(value);
            byte[] jsonBytes = Encoding.ASCII.GetBytes(json);

            this.SaveBytes(key, jsonBytes, overWrite);
        }

        /// <inheritdoc />
        public byte[] LoadBytes(string key)
        {
            byte[] keyBytes = Encoding.ASCII.GetBytes(key);
            byte[] row = this.rocksdb.Get(keyBytes);

            if (row == null)
                return null;

            return row;
        }

        /// <inheritdoc />
        public T LoadValue<T>(string key)
        {
            byte[] bytes = this.LoadBytes(key);

            if (bytes == null)
                return default;

            T value = this.dataStoreSerializer.Deserialize<T>(bytes);
            return value;
        }

        /// <inheritdoc />
        public T LoadValueJson<T>(string key)
        {
            byte[] bytes = this.LoadBytes(key);

            if (bytes == null)
                return default;

            string json = Encoding.ASCII.GetString(bytes);

            T value = Serializer.ToObject<T>(json);

            return value;
        }

        /// <inheritdoc />
        public List<T> GetAllAsJson<T>()
        {
            throw new NotImplementedException();
        }

        /// <inheritdoc />
        public void Dispose()
        {
            this.rocksdb.Dispose();
        }
    }
}