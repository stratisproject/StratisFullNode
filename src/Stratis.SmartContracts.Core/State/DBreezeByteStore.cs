using System;
using DBreeze;
using DBreeze.DataTypes;
using Stratis.Bitcoin.Configuration;
using Stratis.Patricia;

namespace Stratis.SmartContracts.Core.State
{
    /// <summary>
    /// A basic Key/Value store in DBreeze.
    /// </summary>
    public class DBreezeByteStore : ISource<byte[], byte[]>
    {
        private DBreezeEngine engine;
        private string table;
        private string folder;

        public DBreezeByteStore(string folder, string table)
        {
            this.folder = folder;
            this.table = table;
        }

        private DBreezeEngine Engine
        {
            get
            {
                if (this.engine == null)
                    this.engine = new DBreezeEngine(this.folder);

                return this.engine;
            }
        }

        public byte[] Get(byte[] key)
        {
            using (DBreeze.Transactions.Transaction t = this.Engine.GetTransaction())
            {
                Row<byte[], byte[]> row = t.Select<byte[], byte[]>(this.table, key);

                if (row.Exists)
                    return row.Value;

                return null;
            }
        }

        public void Put(byte[] key, byte[] val)
        {
            using (DBreeze.Transactions.Transaction t = this.Engine.GetTransaction())
            {
                t.Insert(this.table, key, val);
                t.Commit();
            }
        }

        public void Delete(byte[] key)
        {
            using (DBreeze.Transactions.Transaction t = this.Engine.GetTransaction())
            {
                t.RemoveKey(this.table, key);
                t.Commit();
            }
        }

        public bool Flush()
        {
            throw new NotImplementedException("Can't flush - no underlying DB");
        }

        /// <summary>
        /// Only use for testing at the moment.
        /// </summary>
        public void Empty()
        {
            using (DBreeze.Transactions.Transaction t = this.Engine.GetTransaction())
            {
                t.RemoveAllKeys(this.table, false);
                t.Commit();
            }
        }
    }

    /// <summary>
    /// Used for dependency injection. A contract state specific implementation of the above class.
    /// </summary>
    public class DBreezeContractStateStore : DBreezeByteStore
    {
        public DBreezeContractStateStore(DataFolder dataFolder) : base(dataFolder.SmartContractStatePath, "state") { }
    }
}
