using System;
using System.Collections.Generic;

namespace Stratis.Bitcoin.Persistence
{
    /// <summary>Allows saving and loading single values to and from key-value storage.</summary>
    public interface IKeyValueRepository : IDisposable
    {
        /// <summary>Persists byte array to the database.</summary>
        void SaveBytes(string key, byte[] bytes, bool overWrite = false);

        /// <summary>Persists any object that <see cref="DBreezeSerializer"/> can serialize to the database.</summary>
        void SaveValue<T>(string key, T value, bool overWrite = false);

        /// <summary>Persists any object to the database. Object is stored as JSON.</summary>
        void SaveValueJson<T>(string key, T value, bool overWrite = false);

        /// <summary>Loads byte array from the database.</summary>
        byte[] LoadBytes(string key);

        /// <summary>Loads an object that <see cref="DBreezeSerializer"/> can deserialize from the database.</summary>
        T LoadValue<T>(string key);

        /// <summary>Loads JSON from the database and deserializes it.</summary>
        T LoadValueJson<T>(string key);

        /// <summary>
        /// Gets all the values from the store.
        /// </summary>
        /// <typeparam name="T">The type to query.</typeparam>
        /// <returns>A list of <typeparamref name="T"/></returns>
        List<T> GetAllAsJson<T>();

        /// <summary> Deletes the given key if it exists.</summary>
        /// <param name="key">The key of the object to delete.</param>.
        void Delete(string key);
    }
}