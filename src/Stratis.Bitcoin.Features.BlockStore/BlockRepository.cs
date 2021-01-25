﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using DBreeze.Utils;
using LevelDB;
using NBitcoin;
using NLog;
using Stratis.Bitcoin.Configuration;
using Stratis.Bitcoin.Interfaces;
using Stratis.Bitcoin.Utilities;

namespace Stratis.Bitcoin.Features.BlockStore
{
    /// <summary>
    /// <see cref="IBlockRepository"/> is the interface to all the logic interacting with the blocks stored in the database.
    /// </summary>
    public interface IBlockRepository : IBlockStore
    {
        /// <summary> The database engine.</summary>
        DB Leveldb { get; }

        /// <summary>
        /// Deletes blocks and indexes for transactions that belong to deleted blocks.
        /// <para>
        /// It should be noted that this does not delete the entries from disk (only the references are removed) and
        /// as such the file size remains the same.
        /// </para>
        /// </summary>
        /// <remarks>LevelDB has its own internal concept of compaction, so we do not need to do anything extra to ensure that the database eventually gets compacted after deletion.</remarks>
        /// <param name="hashes">List of block hashes to be deleted.</param>
        void DeleteBlocks(List<uint256> hashes);

        /// <summary>
        /// Persist the next block hash and insert new blocks into the database.
        /// </summary>
        /// <param name="newTip">Hash and height of the new repository's tip.</param>
        /// <param name="blocks">Blocks to be inserted.</param>
        void PutBlocks(HashHeightPair newTip, List<Block> blocks);

        /// <summary>
        /// Get the blocks from the database by using block hashes.
        /// </summary>
        /// <param name="hashes">A list of unique block hashes.</param>
        /// <returns>The blocks (or null if not found) in the same order as the hashes on input.</returns>
        List<Block> GetBlocks(List<uint256> hashes);

        /// <summary>
        /// Wipe out blocks and their transactions then replace with a new block.
        /// </summary>
        /// <param name="newTip">Hash and height of the new repository's tip.</param>
        /// <param name="hashes">List of all block hashes to be deleted.</param>
        void Delete(HashHeightPair newTip, List<uint256> hashes);

        /// <summary>
        /// Determine if a block already exists
        /// </summary>
        /// <param name="hash">The hash.</param>
        /// <returns><c>true</c> if the block hash can be found in the database, otherwise return <c>false</c>.</returns>
        bool Exist(uint256 hash);

        /// <summary>
        /// Iterate over every block in the database.
        /// If <see cref="TxIndex"/> is true, we store the block hash alongside the transaction hash in the transaction table, otherwise clear the transaction table.
        /// </summary>
        void ReIndex();

        /// <summary>
        /// Set whether to index transactions by block hash, as well as storing them inside of the block.
        /// </summary>
        /// <param name="txIndex">Whether to index transactions.</param>
        void SetTxIndex(bool txIndex);

        /// <summary>Hash and height of the repository's tip.</summary>
        HashHeightPair TipHashAndHeight { get; }

        /// <summary> Indicates that the node should store all transaction data in the database.</summary>
        bool TxIndex { get; }
    }

    public class BlockRepository : IBlockRepository
    {
        internal static readonly byte BlockTableName = 1;
        internal static readonly byte CommonTableName = 2;
        internal static readonly byte TransactionTableName = 3;

        private readonly DB leveldb;

        private object locker;

        private readonly ILogger logger;

        private readonly Network network;

        private static readonly byte[] RepositoryTipKey = new byte[0];

        private static readonly byte[] TxIndexKey = new byte[1];

        /// <inheritdoc />
        public HashHeightPair TipHashAndHeight { get; private set; }

        /// <inheritdoc />
        public bool TxIndex { get; private set; }

        public DB Leveldb => this.leveldb;

        private readonly DBreezeSerializer dBreezeSerializer;
        private readonly IReadOnlyDictionary<uint256, Transaction> genesisTransactions;

        public BlockRepository(Network network, DataFolder dataFolder, DBreezeSerializer dBreezeSerializer)
            : this(network, dataFolder.BlockPath, dBreezeSerializer)
        {
        }

        public BlockRepository(Network network, string folder, DBreezeSerializer dBreezeSerializer)
        {
            Guard.NotNull(network, nameof(network));
            Guard.NotEmpty(folder, nameof(folder));

            Directory.CreateDirectory(folder);
            var options = new Options { CreateIfMissing = true };
            this.leveldb = new DB(options, folder);
            this.locker = new object();

            this.logger = LogManager.GetCurrentClassLogger();
            this.network = network;
            this.dBreezeSerializer = dBreezeSerializer;
            this.genesisTransactions = network.GetGenesis().Transactions.ToDictionary(k => k.GetHash());
        }

        /// <inheritdoc />
        public virtual void Initialize()
        {
            Block genesis = this.network.GetGenesis();

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
                return default(Transaction);
            }

            if (this.genesisTransactions.TryGetValue(trxid, out Transaction genesisTransaction))
                return genesisTransaction;

            try
            {
                Transaction transaction = null;
                lock (this.locker)
                {
                    byte[] transactionRow = this.leveldb.Get(TransactionTableName, trxid.ToBytes());

                    if (transactionRow == null)
                    {
                        this.logger.Trace("(-)[NO_BLOCK]:null");
                        return null;
                    }

                    byte[] blockRow = this.leveldb.Get(BlockTableName, transactionRow);

                    if (blockRow != null)
                    {
                        var block = this.dBreezeSerializer.Deserialize<Block>(blockRow);
                        transaction = block.Transactions.FirstOrDefault(t => t.GetHash() == trxid);
                    }
                }

                return transaction;
            }
            catch (Exception ex)
            {
                this.logger.Error($"An exception occurred: {ex}");
                throw;
            }
        }

        /// <inheritdoc/>
        public Transaction[] GetTransactionsByIds(uint256[] trxids, CancellationToken cancellation = default(CancellationToken))
        {
            if (!this.TxIndex)
            {
                this.logger.Trace("(-)[TX_INDEXING_DISABLED]:null");
                return null;
            }

            try
            {
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

                        byte[] transactionRow = this.leveldb.Get(TransactionTableName, trxids[i].ToBytes());
                        if (transactionRow == null)
                        {
                            this.logger.Trace("(-)[NO_TX_ROW]:null");
                            return null;
                        }

                        byte[] blockRow = this.leveldb.Get(BlockTableName, transactionRow);

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
            catch (Exception ex)
            {
                this.logger.Error($"An exception occurred: {ex}");
                throw;
            }
        }

        /// <inheritdoc />
        public uint256 GetBlockIdByTransactionId(uint256 trxid)
        {
            Guard.NotNull(trxid, nameof(trxid));

            if (!this.TxIndex)
            {
                this.logger.Trace("(-)[NO_TXINDEX]:null");
                return default(uint256);
            }

            if (this.genesisTransactions.ContainsKey(trxid))
            {
                return this.network.GenesisHash;
            }

            try
            {
                uint256 res = null;
                lock (this.locker)
                {
                    byte[] transactionRow = this.leveldb.Get(TransactionTableName, trxid.ToBytes());
                    if (transactionRow != null)
                        res = new uint256(transactionRow);
                }

                return res;
            }
            catch (Exception ex)
            {
                this.logger.Error($"An exception occurred: {ex}");
                throw;
            }
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
                this.logger.Debug("Inserting blocks...");

                // Index blocks.
                foreach (KeyValuePair<uint256, Block> kv in blockList)
                {
                    uint256 blockId = kv.Key;
                    Block block = kv.Value;

                    this.logger.Debug($"Checking insert for block '{kv.Key}'");

                    // If the block is already in store don't write it again.
                    byte[] blockRow = this.leveldb.Get(BlockTableName, blockId.ToBytes());
                    if (blockRow == null)
                    {
                        batch.Put(BlockTableName, blockId.ToBytes(), this.dBreezeSerializer.Serialize(block));

                        if (this.TxIndex)
                        {
                            foreach (Transaction transaction in block.Transactions)
                                transactions.Add((transaction, block));
                        }
                    }
                }

                this.logger.Debug("Batch created...");
                this.leveldb.Write(batch, new WriteOptions() { Sync = true });
                this.logger.Debug("Batch written...");
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
                    batch.Put(TransactionTableName, transaction.GetHash().ToBytes(), block.GetHash().ToBytes());

                this.leveldb.Write(batch, new WriteOptions() { Sync = true });
            }
        }

        public IEnumerable<Block> EnumerateBatch(List<ChainedHeader> headers)
        {
            lock (this.locker)
            {
                foreach (ChainedHeader chainedHeader in headers)
                {
                    byte[] blockRow = this.leveldb.Get(BlockTableName, chainedHeader.HashBlock.ToBytes());
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
                        var enumerator = this.leveldb.GetEnumerator();
                        while (enumerator.MoveNext())
                        {
                            if (enumerator.Current.Key[0] == BlockTableName)
                            {
                                var block = this.dBreezeSerializer.Deserialize<Block>(enumerator.Current.Value);
                                foreach (Transaction transaction in block.Transactions)
                                {
                                    batch.Put(TransactionTableName, transaction.GetHash().ToBytes(), block.GetHash().ToBytes());
                                }

                                // inform the user about the ongoing operation
                                if (++rowCount % 1000 == 0)
                                {
                                    this.logger.Info("Reindex in process... {0}/{1} blocks processed.", rowCount, totalBlocksCount);
                                }
                            }
                        }

                        this.leveldb.Write(batch, new WriteOptions() { Sync = true });
                    }

                    this.logger.Info("Reindex completed successfully.");
                }
                else
                {
                    var enumerator = this.leveldb.GetEnumerator();
                    while (enumerator.MoveNext())
                    {
                        // Clear tx from database.
                        if (enumerator.Current.Key[0] == TransactionTableName)
                            this.leveldb.Delete(enumerator.Current.Key);
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
            byte[] row = this.leveldb.Get(CommonTableName, TxIndexKey);
            if (row != null)
            {
                this.TxIndex = BitConverter.ToBoolean(row, 0);
                res = this.TxIndex;
            }

            return res;
        }

        private void SaveTxIndex(bool txIndex)
        {
            this.TxIndex = txIndex;
            this.leveldb.Put(CommonTableName, TxIndexKey, BitConverter.GetBytes(txIndex));
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
                byte[] row = this.leveldb.Get(CommonTableName, RepositoryTipKey);
                if (row != null)
                    this.TipHashAndHeight = this.dBreezeSerializer.Deserialize<HashHeightPair>(row);
            }

            return this.TipHashAndHeight;
        }

        private void SaveTipHashAndHeight(HashHeightPair newTip)
        {
            this.TipHashAndHeight = newTip;
            this.leveldb.Put(CommonTableName, RepositoryTipKey, this.dBreezeSerializer.Serialize(newTip));
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
                byte[] blockRow = this.leveldb.Get(BlockTableName, key);
                if (blockRow != null)
                    res = true;
            }

            return res;
        }

        protected virtual void OnDeleteTransactions(List<(Transaction, Block)> transactions)
        {
            foreach ((Transaction transaction, Block block) in transactions)
                this.leveldb.Delete(TransactionTableName, transaction.GetHash().ToBytes());
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
                this.leveldb.Delete(BlockTableName, block.GetHash().ToBytes());
        }

        public List<Block> GetBlocksFromHashes(List<uint256> hashes)
        {
            try
            {
                var results = new Dictionary<uint256, Block>();

                // Access hash keys in sorted order.
                var byteListComparer = new ByteListComparer();
                List<(uint256, byte[])> keys = hashes.Select(hash => (hash, hash.ToBytes())).ToList();

                keys.Sort((key1, key2) => byteListComparer.Compare(key1.Item2, key2.Item2));

                this.logger.Debug("GetBlocksFromHashes...");

                foreach ((uint256, byte[]) key in keys)
                {
                    // If searching for genesis block, return it.
                    if (key.Item1 == this.network.GenesisHash)
                    {
                        results[key.Item1] = this.network.GetGenesis();
                        continue;
                    }

                    this.logger.Debug($"Getting block for {key.Item1}");

                    byte[] blockRow = this.leveldb.Get(BlockTableName, key.Item2);
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
            catch (Exception ex)
            {
                this.logger.Error($"An exception occurred: {ex}");
                throw;
            }
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

        /// <inheritdoc />
        public void Dispose()
        {
            this.leveldb.Dispose();
        }
    }
}
