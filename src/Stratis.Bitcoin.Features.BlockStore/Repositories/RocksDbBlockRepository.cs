using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using DBreeze.Utils;
using NBitcoin;
using NLog;
using RocksDbSharp;
using Stratis.Bitcoin.Configuration;
using Stratis.Bitcoin.Persistence;
using Stratis.Bitcoin.Utilities;

namespace Stratis.Bitcoin.Features.BlockStore.Repositories
{
    public class RocksDbBlockRepository : IBlockRepository
    {
        private readonly string dataFolder;
        private DbOptions dbOptions;
        private RocksDb rocksDb;
        private readonly object locker;
        private readonly ILogger logger;
        private readonly Network network;

        private static readonly byte[] RepositoryTipKey = new byte[0];

        private static readonly byte[] TxIndexKey = new byte[1];

        /// <inheritdoc />
        public HashHeightPair TipHashAndHeight { get; private set; }

        /// <inheritdoc />
        public bool TxIndex { get; private set; }

        private readonly DBreezeSerializer dBreezeSerializer;
        private IReadOnlyDictionary<uint256, Transaction> genesisTransactions;

        public RocksDbBlockRepository(Network network, string dataFolder, DBreezeSerializer dataStoreSerializer)
        {
            this.dataFolder = dataFolder;
            this.dBreezeSerializer = dataStoreSerializer;
            this.locker = new object();
            this.logger = LogManager.GetCurrentClassLogger();
            this.network = network;
        }

        public RocksDbBlockRepository(Network network, DataFolder dataFolder, DBreezeSerializer dataStoreSerializer)
            : this(network, dataFolder.BlockPath, dataStoreSerializer)
        {
        }

        /// <inheritdoc />
        public void Initialize()
        {
            Directory.CreateDirectory(this.dataFolder);

            this.dbOptions = new DbOptions().SetCreateIfMissing(true);
            this.rocksDb = RocksDb.Open(this.dbOptions, this.dataFolder);

            Block genesis = this.network.GetGenesis();
            this.genesisTransactions = this.network.GetGenesis().Transactions.ToDictionary(k => k.GetHash());

            lock (this.locker)
            {
                if (this.LoadTipHashAndHeight() == null)
                {
                    this.SaveTipHashAndHeight(new HashHeightPair(genesis.GetHash(), 0));
                }

                if (this.LoadTxIndex() == null)
                {
                    this.SaveTxIndex(false);
                }
            }
        }

        /// <inheritdoc />
        public Transaction GetTransactionById(uint256 trxid)
        {
            Guard.NotNull(trxid, nameof(trxid));

            if (!this.TxIndex)
            {
                this.logger.Trace("(-)[TX_INDEXING_DISABLED]:null");
                return default;
            }

            if (this.genesisTransactions.TryGetValue(trxid, out Transaction genesisTransaction))
            {
                return genesisTransaction;
            }

            Transaction res = null;
            lock (this.locker)
            {
                byte[] transactionRow = this.rocksDb.Get(BlockRepositoryConstants.TransactionTableName, trxid.ToBytes());

                if (transactionRow == null)
                {
                    this.logger.Trace("(-)[NO_BLOCK]:null");
                    return null;
                }

                byte[] blockRow = this.rocksDb.Get(BlockRepositoryConstants.BlockTableName, transactionRow);

                if (blockRow != null)
                {
                    var block = this.dBreezeSerializer.Deserialize<Block>(blockRow);
                    res = block.Transactions.FirstOrDefault(t => t.GetHash() == trxid);
                }
            }

            return res;
        }

        /// <inheritdoc/>
        public Transaction[] GetTransactionsByIds(uint256[] trxids, CancellationToken cancellation = default(CancellationToken))
        {
            if (!this.TxIndex)
            {
                this.logger.Trace("(-)[TX_INDEXING_DISABLED]:null");
                return null;
            }

            Transaction[] txes = new Transaction[trxids.Length];

            lock (this.locker)
            {
                for (int i = 0; i < trxids.Length; i++)
                {
                    cancellation.ThrowIfCancellationRequested();

                    bool alreadyFetched = trxids.Take(i).Any(x => x == trxids[i]);

                    if (alreadyFetched)
                    {
                        this.logger.Debug("Duplicated transaction encountered. Tx id: '{0}'.", trxids[i]);

                        txes[i] = txes.First(x => x.GetHash() == trxids[i]);
                        continue;
                    }

                    if (this.genesisTransactions.TryGetValue(trxids[i], out Transaction genesisTransaction))
                    {
                        txes[i] = genesisTransaction;
                        continue;
                    }

                    byte[] transactionRow = this.rocksDb.Get(BlockRepositoryConstants.TransactionTableName, trxids[i].ToBytes());
                    if (transactionRow == null)
                    {
                        this.logger.Trace("(-)[NO_TX_ROW]:null");
                        return null;
                    }

                    byte[] blockRow = this.rocksDb.Get(BlockRepositoryConstants.BlockTableName, transactionRow);

                    if (blockRow != null)
                    {
                        this.logger.Trace("(-)[NO_BLOCK]:null");
                        return null;
                    }

                    var block = this.dBreezeSerializer.Deserialize<Block>(blockRow);
                    Transaction tx = block.Transactions.FirstOrDefault(t => t.GetHash() == trxids[i]);

                    txes[i] = tx;
                }
            }

            return txes;
        }

        /// <inheritdoc />
        public uint256 GetBlockIdByTransactionId(uint256 trxid)
        {
            Guard.NotNull(trxid, nameof(trxid));

            if (!this.TxIndex)
            {
                this.logger.Trace("(-)[NO_TXINDEX]:null");
                return default;
            }

            if (this.genesisTransactions.ContainsKey(trxid))
            {
                return this.network.GenesisHash;
            }

            uint256 res = null;
            lock (this.locker)
            {
                byte[] transactionRow = this.rocksDb.Get(BlockRepositoryConstants.TransactionTableName, trxid.ToBytes());
                if (transactionRow != null)
                    res = new uint256(transactionRow);
            }

            return res;
        }

        protected virtual void OnInsertBlocks(List<Block> blocks)
        {
            var transactions = new List<(Transaction, Block)>();
            var byteListComparer = new ByteListComparer();
            var blockDict = new Dictionary<uint256, Block>();

            // Gather blocks.
            foreach (Block block in blocks)
            {
                uint256 blockId = block.GetHash();
                blockDict[blockId] = block;
            }

            // Sort blocks. Be consistent in always converting our keys to byte arrays using the ToBytes method.
            List<KeyValuePair<uint256, Block>> blockList = blockDict.ToList();
            blockList.Sort((pair1, pair2) => byteListComparer.Compare(pair1.Key.ToBytes(), pair2.Key.ToBytes()));

            using (var batch = new WriteBatch())
            {
                // Index blocks.
                foreach (KeyValuePair<uint256, Block> kv in blockList)
                {
                    uint256 blockId = kv.Key;
                    Block block = kv.Value;

                    // If the block is already in store don't write it again.
                    byte[] blockRow = this.rocksDb.Get(BlockRepositoryConstants.BlockTableName, blockId.ToBytes());
                    if (blockRow == null)
                    {
                        batch.Put(BlockRepositoryConstants.BlockTableName, blockId.ToBytes(), this.dBreezeSerializer.Serialize(block));

                        if (this.TxIndex)
                        {
                            foreach (Transaction transaction in block.Transactions)
                                transactions.Add((transaction, block));
                        }
                    }
                }

                this.rocksDb.Write(batch);
            }

            if (this.TxIndex)
                this.OnInsertTransactions(transactions);
        }

        protected virtual void OnInsertTransactions(List<(Transaction, Block)> transactions)
        {
            var byteListComparer = new ByteListComparer();
            transactions.Sort((pair1, pair2) => byteListComparer.Compare(pair1.Item1.GetHash().ToBytes(), pair2.Item1.GetHash().ToBytes()));

            using (var batch = new WriteBatch())
            {
                // Index transactions.
                foreach ((Transaction transaction, Block block) in transactions)
                    batch.Put(BlockRepositoryConstants.TransactionTableName, transaction.GetHash().ToBytes(), block.GetHash().ToBytes());

                this.rocksDb.Write(batch);
            }
        }

        public IEnumerable<Block> EnumerateBatch(List<ChainedHeader> headers)
        {
            lock (this.locker)
            {
                foreach (ChainedHeader chainedHeader in headers)
                {
                    byte[] blockRow = this.rocksDb.Get(BlockRepositoryConstants.BlockTableName, chainedHeader.HashBlock.ToBytes());
                    Block block = blockRow != null ? this.dBreezeSerializer.Deserialize<Block>(blockRow) : null;
                    yield return block;
                }
            }
        }

        /// <inheritdoc />
        public void ReIndex()
        {
            lock (this.locker)
            {
                if (this.TxIndex)
                {
                    int rowCount = 0;
                    // Insert transactions to database.

                    int totalBlocksCount = this.TipHashAndHeight?.Height ?? 0;

                    var warningMessage = new StringBuilder();
                    warningMessage.AppendLine("".PadRight(59, '=') + " W A R N I N G " + "".PadRight(59, '='));
                    warningMessage.AppendLine();
                    warningMessage.AppendLine($"Starting ReIndex process on a total of {totalBlocksCount} blocks.");
                    warningMessage.AppendLine("The operation could take a long time, please don't stop it.");
                    warningMessage.AppendLine();
                    warningMessage.AppendLine("".PadRight(133, '='));
                    warningMessage.AppendLine();

                    this.logger.Info(warningMessage.ToString());
                    using (var batch = new WriteBatch())
                    {
                        var enumerator = this.rocksDb.NewIterator();
                        for (enumerator.SeekToFirst(); enumerator.Valid(); enumerator.Next())
                        {
                            if (enumerator.Key()[0] == BlockRepositoryConstants.BlockTableName)
                            {
                                var block = this.dBreezeSerializer.Deserialize<Block>(enumerator.Value());
                                foreach (Transaction transaction in block.Transactions)
                                {
                                    batch.Put(BlockRepositoryConstants.TransactionTableName, transaction.GetHash().ToBytes(), block.GetHash().ToBytes());
                                }

                                // inform the user about the ongoing operation
                                if (++rowCount % 1000 == 0)
                                {
                                    this.logger.Info("Reindex in process... {0}/{1} blocks processed.", rowCount, totalBlocksCount);
                                }
                            }
                        }

                        this.rocksDb.Write(batch);
                    }

                    this.logger.Info("Reindex completed successfully.");
                }
                else
                {
                    var enumerator = this.rocksDb.NewIterator();
                    for (enumerator.SeekToFirst(); enumerator.Valid(); enumerator.Next())
                    {
                        // Clear tx from database.
                        if (enumerator.Key()[0] == BlockRepositoryConstants.TransactionTableName)
                            this.rocksDb.Remove(enumerator.Key());
                    }
                }
            }
        }

        /// <inheritdoc />
        public void PutBlocks(HashHeightPair newTip, List<Block> blocks)
        {
            Guard.NotNull(newTip, nameof(newTip));
            Guard.NotNull(blocks, nameof(blocks));

            // DBreeze is faster if sort ascending by key in memory before insert
            // however we need to find how byte arrays are sorted in DBreeze.
            lock (this.locker)
            {
                this.OnInsertBlocks(blocks);

                // Commit additions
                this.SaveTipHashAndHeight(newTip);
            }
        }

        private bool? LoadTxIndex()
        {
            bool? res = null;
            byte[] row = this.rocksDb.Get(BlockRepositoryConstants.CommonTableName, TxIndexKey);
            if (row != null)
            {
                this.TxIndex = BitConverter.ToBoolean(row);
                res = this.TxIndex;
            }

            return res;
        }

        private void SaveTxIndex(bool txIndex)
        {
            this.TxIndex = txIndex;
            this.rocksDb.Put(BlockRepositoryConstants.CommonTableName, TxIndexKey, BitConverter.GetBytes(txIndex));
        }

        /// <inheritdoc />
        public void SetTxIndex(bool txIndex)
        {
            lock (this.locker)
            {
                this.SaveTxIndex(txIndex);
            }
        }

        private HashHeightPair LoadTipHashAndHeight()
        {
            if (this.TipHashAndHeight == null)
            {
                byte[] row = this.rocksDb.Get(BlockRepositoryConstants.CommonTableName, RepositoryTipKey);
                if (row != null)
                    this.TipHashAndHeight = this.dBreezeSerializer.Deserialize<HashHeightPair>(row);
            }

            return this.TipHashAndHeight;
        }

        private void SaveTipHashAndHeight(HashHeightPair newTip)
        {
            this.TipHashAndHeight = newTip;
            this.rocksDb.Put(BlockRepositoryConstants.CommonTableName, RepositoryTipKey, this.dBreezeSerializer.Serialize(newTip));
        }

        /// <inheritdoc />
        public Block GetBlock(uint256 hash)
        {
            Guard.NotNull(hash, nameof(hash));

            Block res = null;
            lock (this.locker)
            {
                var results = this.GetBlocksFromHashes(new List<uint256> { hash });

                if (results.FirstOrDefault() != null)
                    res = results.FirstOrDefault();
            }

            return res;
        }

        /// <inheritdoc />
        public List<Block> GetBlocks(List<uint256> hashes)
        {
            Guard.NotNull(hashes, nameof(hashes));

            List<Block> blocks;

            lock (this.locker)
            {
                blocks = this.GetBlocksFromHashes(hashes);
            }

            return blocks;
        }

        /// <inheritdoc />
        public bool Exist(uint256 hash)
        {
            Guard.NotNull(hash, nameof(hash));

            bool res = false;
            lock (this.locker)
            {
                // Lazy loading is on so we don't fetch the whole value, just the row.
                byte[] key = hash.ToBytes();
                byte[] blockRow = this.rocksDb.Get(BlockRepositoryConstants.BlockTableName, key);
                if (blockRow != null)
                    res = true;
            }

            return res;
        }

        protected virtual void OnDeleteTransactions(List<(Transaction, Block)> transactions)
        {
            foreach ((Transaction transaction, Block block) in transactions)
                this.rocksDb.Remove(BlockRepositoryConstants.TransactionTableName, transaction.GetHash().ToBytes());
        }

        protected virtual void OnDeleteBlocks(List<Block> blocks)
        {
            if (this.TxIndex)
            {
                var transactions = new List<(Transaction, Block)>();

                foreach (Block block in blocks)
                    foreach (Transaction transaction in block.Transactions)
                        transactions.Add((transaction, block));

                this.OnDeleteTransactions(transactions);
            }

            foreach (Block block in blocks)
                this.rocksDb.Remove(BlockRepositoryConstants.BlockTableName, block.GetHash().ToBytes());
        }

        public List<Block> GetBlocksFromHashes(List<uint256> hashes)
        {
            var results = new Dictionary<uint256, Block>();

            // Access hash keys in sorted order.
            var byteListComparer = new ByteListComparer();
            List<(uint256, byte[])> keys = hashes.Select(hash => (hash, hash.ToBytes())).ToList();

            keys.Sort((key1, key2) => byteListComparer.Compare(key1.Item2, key2.Item2));

            foreach ((uint256, byte[]) key in keys)
            {
                // If searching for genesis block, return it.
                if (key.Item1 == this.network.GenesisHash)
                {
                    results[key.Item1] = this.network.GetGenesis();
                    continue;
                }

                byte[] blockRow = this.rocksDb.Get(BlockRepositoryConstants.BlockTableName, key.Item2);
                if (blockRow != null)
                {
                    results[key.Item1] = this.dBreezeSerializer.Deserialize<Block>(blockRow);

                    this.logger.Debug("Block hash '{0}' loaded from the store.", key.Item1);
                }
                else
                {
                    results[key.Item1] = null;

                    this.logger.Debug("Block hash '{0}' not found in the store.", key.Item1);
                }
            }

            // Return the result in the order that the hashes were presented.
            return hashes.Select(hash => results[hash]).ToList();
        }

        /// <inheritdoc />
        public void Delete(HashHeightPair newTip, List<uint256> hashes)
        {
            Guard.NotNull(newTip, nameof(newTip));
            Guard.NotNull(hashes, nameof(hashes));

            lock (this.locker)
            {
                List<Block> blocks = this.GetBlocksFromHashes(hashes);
                this.OnDeleteBlocks(blocks.Where(b => b != null).ToList());
                this.SaveTipHashAndHeight(newTip);
            }
        }

        /// <inheritdoc />
        public void DeleteBlocks(List<uint256> hashes)
        {
            Guard.NotNull(hashes, nameof(hashes));

            lock (this.locker)
            {
                List<Block> blocks = this.GetBlocksFromHashes(hashes);

                this.OnDeleteBlocks(blocks.Where(b => b != null).ToList());
            }
        }

        public byte[] Get(byte tableName, byte[] key)
        {
            byte[] result = this.rocksDb.Get(tableName, key);
            return result;
        }

        public void Put(byte tableName, byte[] key, byte[] value)
        {
            this.rocksDb.Put(tableName, key, value);
        }

        /// <inheritdoc />
        public void Dispose()
        {
            this.rocksDb.Dispose();
        }
    }
}