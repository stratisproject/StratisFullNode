using System.Collections.Generic;
using System.IO;
using System.Text;
using Stratis.Bitcoin.Configuration;
using Stratis.Bitcoin.Database;
using Stratis.Bitcoin.Utilities;
using Stratis.Bitcoin.Utilities.JsonConverters;

namespace Stratis.Bitcoin.Persistence.KeyValueStores
{
    public class KeyValueRepository<T> : IKeyValueRepository where T : IDb, new()
    {
        /// <summary>Access to database.</summary>
        private readonly IDb db;

        private readonly DBreezeSerializer dBreezeSerializer;

        public KeyValueRepository(DataFolder dataFolder, DBreezeSerializer dBreezeSerializer) : this(dataFolder.KeyValueRepositoryPath, dBreezeSerializer)
        {
        }

        public KeyValueRepository(string folder, DBreezeSerializer dBreezeSerializer)
        {
            Directory.CreateDirectory(folder);
            this.dBreezeSerializer = dBreezeSerializer;

            // Open a connection to a new DB and create if not found
            this.db = new T();
            this.db.Open(folder);
        }

        /// <inheritdoc />
        public void SaveBytes(string key, byte[] bytes, bool overWrite = false)
        {
            using (var batch = this.db.GetWriteBatch())
            {
                byte[] keyBytes = Encoding.ASCII.GetBytes(key);

                if (overWrite)
                {
                    byte[] row = this.db.Get(keyBytes);
                    if (row != null)
                        batch.Delete(keyBytes);
                }

                batch.Put(keyBytes, bytes);
                batch.Write();
            }
        }

        /// <inheritdoc />
        public void SaveValue<T>(string key, T value, bool overWrite = false)
        {
            this.SaveBytes(key, this.dBreezeSerializer.Serialize(value), overWrite);
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

            byte[] row = this.db.Get(keyBytes);

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

            T value = this.dBreezeSerializer.Deserialize<T>(bytes);
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
        public List<T> GetAllAsJson<T>()
        {
            var values = new List<T>();

            using (var iterator = this.db.GetIterator())
            {
                foreach ((byte[] key, byte[] value) in iterator.GetAll())
                {
                    if (value == null)
                        continue;

                    string json = Encoding.ASCII.GetString(value);
                    values.Add(Serializer.ToObject<T>(json));
                }
            }

            return values;
        }

        /// <inheritdoc />
        public void Delete(string key)
        {
            byte[] keyBytes = Encoding.ASCII.GetBytes(key);

            byte[] row = this.db.Get(keyBytes);
            if (row != null)
            {
                using (var batch = this.db.GetWriteBatch())
                {
                    batch.Delete(keyBytes);
                    batch.Write();
                }
            }
        }

        /// <inheritdoc />
        public void Dispose()
        {
            this.db.Dispose();
        }
    }
}