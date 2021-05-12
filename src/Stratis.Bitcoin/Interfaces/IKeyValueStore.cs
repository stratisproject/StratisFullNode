using System;

namespace Stratis.Bitcoin.Interfaces
{
    /// <summary>Supported transaction modes.</summary>
    public enum KeyValueStoreTransactionMode
    {
        Read,
        ReadWrite
    }

    /// <summary>
    /// Primary interface methods of a key-value store.
    /// </summary>
    public interface IKeyValueStore : IDisposable
    {
        /// <summary>
        /// Get the names of the tables in the repository.
        /// </summary>
        /// <returns></returns>
        string[] GetTables();

        /// <summary>
        /// The transaction factory for this key-store type.
        /// </summary>
        /// <param name="mode">The transaction mode.</param>
        /// <param name="tables">The tables that will be updated if <paramref name="mode"/> is <see cref="KeyValueStoreTransactionMode.ReadWrite"/>.</param>
        /// <returns>A transaction specific to the key-store type.</returns>
        IKeyValueStoreTransaction CreateTransaction(KeyValueStoreTransactionMode mode, params string[] tables);
    }
}