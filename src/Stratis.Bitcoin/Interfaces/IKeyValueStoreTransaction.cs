using System;
using System.Collections.Generic;

namespace Stratis.Bitcoin.Interfaces
{
    public enum SortOrder
    {
        Ascending,
        Descending,
        Unsorted
    }

    /// <summary>
    /// The high-level methods for manipulating values in the key-value store.
    /// </summary>
    public interface IKeyValueStoreTransaction : IDisposable
    {
        void Insert<TKey, TObject>(string tableName, TKey key, TObject obj);

        void InsertMultiple<TKey, TObject>(string tableName, (TKey, TObject)[] objects);

        void InsertDictionary<TKey, TObject>(string tableName, Dictionary<TKey, TObject> objects);

        bool Select<TKey, TObject>(string tableName, TKey key, out TObject obj);

        List<TObject> SelectMultiple<TKey, TObject>(string tableName, TKey[] keys);

        Dictionary<TKey, TObject> SelectDictionary<TKey, TObject>(string tableName);

        IEnumerable<(TKey, TObject)> SelectAll<TKey, TObject>(string tableName, bool keysOnly = false, SortOrder sortOrder = SortOrder.Ascending);

        IEnumerable<(TKey, TObject)> SelectForward<TKey, TObject>(string tableName, TKey firstKey, bool includeFirstKey = true, bool keysOnly = false);

        IEnumerable<(TKey, TObject)> SelectBackward<TKey, TObject>(string tableName, TKey lastKey, bool includeLastKey = true, bool keysOnly = false);

        IEnumerable<(TKey, TObject)> SelectForward<TKey, TObject>(string tableName, TKey firstKey, TKey lastKey, bool includeFirstKey = true, bool includeLastKey = true, bool keysOnly = false);

        IEnumerable<(TKey, TObject)> SelectBackward<TKey, TObject>(string tableName, TKey firstKey, TKey lastKey, bool includeFirstKey = true, bool includeLastKey = true, bool keysOnly = false);

        void RemoveKey<TKey, TObject>(string tableName, TKey key, TObject obj);

        int Count(string tableName);

        void RemoveAllKeys(string tableName);

        bool Exists<TKey>(string tableName, TKey key);

        bool[] ExistsMultiple<TKey>(string tableName, TKey[] keys);

        void Commit();

        void Rollback();

        string ToString();
    }
}
