using System;

namespace Stratis.Bitcoin.Database
{
    /// <summary>
    /// This interface and its relevant implementations provide a standardized interface to databases such as <see cref="RocksDb"/> and <see cref="LevelDb"/>, or other databases
    /// capable of supporting key-based value retrieval and key iteration.
    /// </summary>
    /// <remarks>
    /// The interface expects keys to be specified as separate table and key identifiers. Similarly iterators are expected to be constrained to operate within single tables.
    /// </remarks>
    public interface IDb : IDisposable
    {
        /// <summary>
        /// Opens the database at the specified path.
        /// </summary>
        /// <param name="dbPath">The path where the database is located.</param>
        void Open(string dbPath);

        /// <summary>
        /// Gets the value associated with a table and key.
        /// </summary>
        /// <param name="table">The table identifier.</param>
        /// <param name="key">The key of the value to retrieve.</param>
        /// <returns>The value for the specified table and key.</returns>
        byte[] Get(byte table, byte[] key);

        /// <summary>
        /// Gets an iterator that allows iteration over keys in a table.
        /// </summary>
        /// <param name="table">The table that will be iterated.</param>
        /// <returns>See <see cref="IDbIterator"/>.</returns>
        IDbIterator GetIterator(byte table);

        /// <summary>
        /// Gets a batch that can be used to record changes that can be applied atomically.
        /// </summary>
        /// <remarks>The <see cref="IDb.Get"/> method will not reflect these changes until they are committed. Use
        /// the <see cref="ReadWriteBatch"/> class if uncommitted changes need to be accessed.</remarks>
        /// <returns>See <see cref="IDbBatch"/>.</returns>
        IDbBatch GetWriteBatch();

        /// <summary>
        /// Removes all tables and their contents.
        /// </summary>
        void Clear();
    }

    /// <summary>
    /// A batch that can be used to record changes that can be applied atomically.
    /// </summary>
    /// <remarks>The database's <see cref="Get"/> method will not reflect these changes until they are committed.</remarks>
    public interface IDbBatch : IDisposable
    {
        /// <summary>
        /// Records a value that will be written to the database when the <see cref="Write"/> method is invoked.
        /// </summary>
        /// <param name="table">The table that will be updated.</param>
        /// <param name="key">The table key that identifies the value to be updated.</param>
        /// <param name="value">The value to be written to the table.</param>
        /// <returns>This class for fluent operations.</returns>
        IDbBatch Put(byte table, byte[] key, byte[] value);

        /// <summary>
        /// Records a key that will be deleted from the database when the <see cref="Write"/> method is invoked.
        /// </summary>
        /// <param name="table">The table that will be updated.</param>
        /// <param name="key">The table key that will be removed.</param>
        /// <returns>This class for fluent operations.</returns>
        IDbBatch Delete(byte table, byte[] key);

        /// <summary>
        /// Writes the recorded changes to the database.
        /// </summary>
        void Write();
    }

    /// <summary>
    /// An iterator that can be used to iterate the keys and values in an <see cref="IDb"/> compliant database.
    /// </summary>
    public interface IDbIterator : IDisposable
    {
        /// <summary>
        /// Seeks to a first key >= <paramref name="key"/> in the relevant table.
        /// If no such key is found then <see cref="IsValid"/> will return <c>false</c>.
        /// </summary>
        /// <param name="key">The key to find.</param>
        void Seek(byte[] key);

        /// <summary>
        /// Seeks to the last key in the relevant table.
        /// If no such key is found then <see cref="IsValid"/> will return <c>false</c>.
        /// </summary>
        void SeekToLast();

        /// <summary>
        /// Seeks to the next key in the relevant table.
        /// If no such key is found then <see cref="IsValid"/> will return <c>false</c>.
        /// </summary>
        void Next();

        /// <summary>
        /// Seeks to the previous key in the relevant table.
        /// If no such key is found then <see cref="IsValid"/> will return <c>false</c>.
        /// </summary>
        void Prev();

        /// <summary>
        /// Determines if the current key is valid.
        /// </summary>
        /// <returns><c>true</c> if a <see cref="Seek"/>, <see cref="Next"/>, <see cref="SeekToLast"/> or <see cref="Prev"/> operation found a valid key. <c>false</c> otherwise.</returns>
        bool IsValid();

        /// <summary>
        /// The current key.
        /// </summary>
        /// <returns>The key.</returns>
        byte[] Key();

        /// <summary>
        /// The current value.
        /// </summary>
        /// <returns>The value.</returns>
        byte[] Value();
    }
}
