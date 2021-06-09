using System.IO;
using System.Text;
using LevelDB;
using Stratis.Bitcoin.Configuration;
using Stratis.Bitcoin.Utilities;
using Stratis.Bitcoin.Utilities.JsonConverters;

namespace Stratis.Bitcoin.Persistence.KeyValueStores
{
    public class LevelDbKeyValueRepository : IKeyValueRepository
    {
        /// <summary>Access to database.</summary>
        private readonly DB leveldb;

        private readonly DBreezeSerializer dBreezeSerializer;

        public LevelDbKeyValueRepository(DataFolder dataFolder, DBreezeSerializer dBreezeSerializer) : this(dataFolder.KeyValueRepositoryPath, dBreezeSerializer)
        {
        }

        public LevelDbKeyValueRepository(string folder, DBreezeSerializer dBreezeSerializer)
        {
            Directory.CreateDirectory(folder);
            this.dBreezeSerializer = dBreezeSerializer;

            // Open a connection to a new DB and create if not found
            var options = new Options { CreateIfMissing = true };
            this.leveldb = new DB(options, folder);
        }

        /// <inheritdoc />
        public void SaveBytes(string key, byte[] bytes, bool overWrite = false)
        {
            byte[] keyBytes = Encoding.ASCII.GetBytes(key);

            if (overWrite)
            {
                byte[] row = this.leveldb.Get(keyBytes);
                if (row != null)
                    this.leveldb.Delete(keyBytes);
            }

            this.leveldb.Put(keyBytes, bytes);
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

            byte[] row = this.leveldb.Get(keyBytes);

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
        public void Dispose()
        {
            this.leveldb.Dispose();
        }
    }
}