﻿using System.IO;
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

        public RocksDbKeyValueRepository(DataFolder dataFolder, DBreezeSerializer dataStoreSerializer)
        {
            this.dataFolder = dataFolder.KeyValueRepositoryPath;
            Directory.CreateDirectory(this.dataFolder);
            this.dataStoreSerializer = dataStoreSerializer;
            this.dbOptions = new DbOptions().SetCreateIfMissing(true);
        }

        /// <inheritdoc />
        public void SaveBytes(string key, byte[] bytes)
        {
            byte[] keyBytes = Encoding.ASCII.GetBytes(key);

            using var rocksdb = RocksDb.Open(this.dbOptions, this.dataFolder);
            rocksdb.Put(keyBytes, bytes);
        }

        /// <inheritdoc />
        public void SaveValue<T>(string key, T value)
        {
            this.SaveBytes(key, this.dataStoreSerializer.Serialize(value));
        }

        /// <inheritdoc />
        public void SaveValueJson<T>(string key, T value)
        {
            string json = Serializer.ToString(value);
            byte[] jsonBytes = Encoding.ASCII.GetBytes(json);

            this.SaveBytes(key, jsonBytes);
        }

        /// <inheritdoc />
        public byte[] LoadBytes(string key)
        {
            byte[] keyBytes = Encoding.ASCII.GetBytes(key);

            using var rocksdb = RocksDb.Open(this.dbOptions, this.dataFolder);
            byte[] row = rocksdb.Get(keyBytes);

            if (row == null)
                return null;

            return row;
        }

        /// <inheritdoc />
        public T LoadValue<T>(string key)
        {
            byte[] bytes = this.LoadBytes(key);

            if (bytes == null)
                return default(T);

            T value = this.dataStoreSerializer.Deserialize<T>(bytes);
            return value;
        }

        /// <inheritdoc />
        public T LoadValueJson<T>(string key)
        {
            byte[] bytes = this.LoadBytes(key);

            if (bytes == null)
                return default(T);

            string json = Encoding.ASCII.GetString(bytes);

            T value = Serializer.ToObject<T>(json);

            return value;
        }

        /// <inheritdoc />
        public void Dispose()
        {
        }
    }
}