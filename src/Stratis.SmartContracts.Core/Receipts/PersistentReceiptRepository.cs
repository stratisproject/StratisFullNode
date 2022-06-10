using System.Collections.Generic;
using System.IO;
using DBreeze;
using NBitcoin;
using Stratis.Bitcoin.Configuration;

namespace Stratis.SmartContracts.Core.Receipts
{
    public class PersistentReceiptRepository : IReceiptRepository
    {
        private const string TableName = "receipts";
        private DBreezeEngine engine;
        private readonly string folder;

        public PersistentReceiptRepository(DataFolder dataFolder)
        {
            this.folder = dataFolder.SmartContractStatePath + TableName;
            Directory.CreateDirectory(this.folder);
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
            

        // TODO: Handle pruning old data in case of reorg.

        /// <inheritdoc />
        public void Store(IEnumerable<Receipt> receipts)
        {
            using (DBreeze.Transactions.Transaction t = this.Engine.GetTransaction())
            {
                foreach(Receipt receipt in receipts)
                {
                    t.Insert<byte[], byte[]>(TableName, receipt.TransactionHash.ToBytes(), receipt.ToStorageBytesRlp());
                }
                t.Commit();
            }
        }

        /// <inheritdoc />
        public Receipt Retrieve(uint256 hash)
        {
            using (DBreeze.Transactions.Transaction t = this.Engine.GetTransaction())
            {
                return this.GetReceipt(t, hash);
            }
        }

        /// <inheritdoc />
        public IList<Receipt> RetrieveMany(IList<uint256> hashes)
        {
            List<Receipt> ret = new List<Receipt>();
            using (DBreeze.Transactions.Transaction t = this.Engine.GetTransaction())
            {
                foreach(uint256 hash in hashes)
                {
                    ret.Add(this.GetReceipt(t, hash));
                }

                return ret;
            }
        }

        private Receipt GetReceipt(DBreeze.Transactions.Transaction t, uint256 hash)
        {
            byte[] result = t.Select<byte[], byte[]>(TableName, hash.ToBytes()).Value;

            if (result == null)
                return null;

            return Receipt.FromStorageBytesRlp(result);
        }
    }
}
