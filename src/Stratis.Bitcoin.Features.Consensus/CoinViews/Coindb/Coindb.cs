using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Microsoft.Extensions.Logging;
using Stratis.Bitcoin.Consensus;
using NBitcoin;
using Stratis.Bitcoin.Configuration;
using Stratis.Bitcoin.Configuration.Logging;
using Stratis.Bitcoin.Database;
using Stratis.Bitcoin.Interfaces;
using Stratis.Bitcoin.Utilities;

namespace Stratis.Bitcoin.Features.Consensus.CoinViews
{
    /// <summary>
    /// Persistent implementation of coinview using an <see cref="IDb"/> database engine.
    /// </summary>
    /// <typeparam name="T">A database supporting the <see cref="IDb"/> interface.</typeparam>
    public class Coindb<T> : ICoindb, IStakedb, IDisposable where T : IDb, new()
    {
        /// <summary>Database key under which the block hash of the coin view's current tip is stored.</summary>
        private static readonly byte[] blockHashKey = new byte[0];

        private static readonly byte coinsTable = 1;
        private static readonly byte blockTable = 2;
        private static readonly byte rewindTable = 3;
        private static readonly byte stakeTable = 4;
        private static readonly byte balanceTable = 5;
        private static readonly byte balanceAdjustmentTable = 6;

        /// <summary>Database key under which the block hash of the coin view's last indexed tip is stored.</summary>
        private static readonly byte[] blockIndexedHashKey = new byte[1];

        private readonly string dataFolder;

        /// <summary>Instance logger.</summary>
        private readonly ILogger logger;

        /// <summary>Specification of the network the node runs on - regtest/testnet/mainnet.</summary>
        private readonly Network network;

        /// <summary>Indicates whether balance indexing is enabled.</summary>
        public bool BalanceIndexingEnabled { get; private set; }

        /// <summary>Hash of the block which is currently the tip of the coinview.</summary>
        private HashHeightPair persistedCoinviewTip;

        /// <summary>The script address reader.</summary>
        private readonly IScriptAddressReader scriptAddressReader;

        /// <summary>Performance counter to measure performance of the database insert and query operations.</summary>
        private readonly BackendPerformanceCounter performanceCounter;

        private BackendPerformanceSnapshot latestPerformanceSnapShot;

        /// <summary>Access to database.</summary>
        private IDb coinDb;

        private readonly DBreezeSerializer dBreezeSerializer;

        private const int MaxRewindBatchSize = 10000;

        public Coindb(Network network, DataFolder dataFolder, IDateTimeProvider dateTimeProvider,
            INodeStats nodeStats, DBreezeSerializer dBreezeSerializer, IScriptAddressReader scriptAddressReader)
            : this(network, dataFolder.CoindbPath, dateTimeProvider, nodeStats, dBreezeSerializer, scriptAddressReader)
        {
        }

        public Coindb(Network network, string dataFolder, IDateTimeProvider dateTimeProvider,
            INodeStats nodeStats, DBreezeSerializer dBreezeSerializer, IScriptAddressReader scriptAddressReader)
        {
            Guard.NotNull(network, nameof(network));
            Guard.NotEmpty(dataFolder, nameof(dataFolder));

            this.dataFolder = dataFolder;
            this.dBreezeSerializer = dBreezeSerializer;
            this.logger = LogManager.GetCurrentClassLogger();
            this.network = network;
            this.performanceCounter = new BackendPerformanceCounter(dateTimeProvider);
            this.scriptAddressReader = scriptAddressReader;

            if (nodeStats.DisplayBenchStats)
                nodeStats.RegisterStats(this.AddBenchStats, StatsType.Benchmark, this.GetType().Name, 400);
        }

        /// <inheritdoc />
        public void Initialize(bool balanceIndexingEnabled)
        {
            // Open a connection to a new DB and create if not found
            this.coinDb = new T();
            this.coinDb.Open(this.dataFolder);

            this.BalanceIndexingEnabled = balanceIndexingEnabled;

            EndiannessFix();

            EnsureCoinDatabaseIntegrity();

            Block genesis = this.network.GetGenesis();

            if (this.GetTipHash() == null)
            {
                using (var batch = this.coinDb.GetWriteBatch())
                {
                    this.SetBlockHash(batch, new HashHeightPair(genesis.GetHash(), 0));
                    batch.Write();
                }
            }

            this.logger.LogInformation("Coin database initialized with tip '{0}'.", this.persistedCoinviewTip);
        }

        private void EndiannessFix()
        {
            // Check if key bytes are in the wrong endian order.
            HashHeightPair current = this.GetTipHash();

            if (current != null)
            {
                byte[] row = this.coinDb.Get(rewindTable, BitConverter.GetBytes(current.Height));
                // Fix the table if required.
                if (row != null)
                {
                    // To be sure, check the next height too.
                    byte[] row2 = (current.Height > 1) ? this.coinDb.Get(rewindTable, BitConverter.GetBytes(current.Height - 1)) : new byte[] { };
                    if (row2 != null)
                    {
                        this.logger.LogInformation("Fixing the coin db.");

                        var rows = new Dictionary<int, byte[]>();

                        using (var iterator = this.coinDb.GetIterator(rewindTable))
                        {
                            iterator.SeekFirst();

                            while (iterator.IsValid())
                            {
                                byte[] key = iterator.Key();

                                if (key.Length != 4)
                                    break;

                                int height = BitConverter.ToInt32(key);

                                rows[height] = iterator.Value();

                                iterator.Next();
                            }
                        }

                        using (var batch = this.coinDb.GetWriteBatch())
                        {
                            foreach (int height in rows.Keys.OrderBy(k => k))
                            {
                                batch.Delete(rewindTable, BitConverter.GetBytes(height));
                            }

                            foreach (int height in rows.Keys.OrderBy(k => k))
                            {
                                batch.Put(rewindTable, BitConverter.GetBytes(height).Reverse().ToArray(), rows[height]);
                            }

                            batch.Write();
                        }
                    }
                }
            }
        }

        /// <summary>Just check the integrity. Coin view performs the sync with the chain tip.</summary>
        private void EnsureCoinDatabaseIntegrity()
        {
            this.logger.LogInformation("Checking coin database integrity...");

            this.EndiannessFix();

            if (this.GetTipHash() == null)
            {
                this.logger.LogInformation($"Rebuilding coin database that has no tip information.");
                this.coinDb.Clear();
                return;
            }

            HashHeightPair maxHeight = new HashHeightPair(this.persistedCoinviewTip.Hash, this.persistedCoinviewTip.Height);

            // If the balance table is empty then rebuild the coin db.
            if (this.BalanceIndexingEnabled)
            {
                HashHeightPair indexedTipHash = this.GetIndexedTipHash();
                if (indexedTipHash == null)
                {
                    this.logger.LogInformation($"Rebuilding coin database to include balance information.");
                    this.coinDb.Clear();
                    return;
                }

                if (indexedTipHash.Height < maxHeight.Height)
                {
                    this.logger.LogInformation($"Rewinding the coin database to include missing balance information.");
                    maxHeight = indexedTipHash;
                }
            }

            for (int height = this.persistedCoinviewTip.Height; height > maxHeight.Height;)
            {
                this.logger.LogInformation($"Fixing coin database, deleting rewind data at height {height} above tip '{maxHeight}'.");

                // Do a batch of rewinding.
                height = RewindInternal(height, maxHeight).Height;
            }

            this.logger.LogInformation("Coin database integrity good.");
        }

        private void SetBlockHash(IDbBatch batch, HashHeightPair nextBlockHash, bool forceUpdateIndexedHeight = false)
        {
            this.persistedCoinviewTip = nextBlockHash;
            batch.Put(blockTable, blockHashKey, nextBlockHash.ToBytes());
            if (this.BalanceIndexingEnabled || forceUpdateIndexedHeight)
                batch.Put(blockTable, blockIndexedHashKey, nextBlockHash.ToBytes());
        }

        public HashHeightPair GetTipHash()
        {
            if (this.persistedCoinviewTip == null)
            {
                var row = this.coinDb.Get(blockTable, blockHashKey);
                if (row != null)
                {
                    this.persistedCoinviewTip = new HashHeightPair();
                    this.persistedCoinviewTip.FromBytes(row);
                }
            }

            return this.persistedCoinviewTip;
        }

        private HashHeightPair GetIndexedTipHash()
        {
            var row = this.coinDb.Get(blockTable, blockIndexedHashKey);
            if (row != null)
            {
                var tip = new HashHeightPair();
                tip.FromBytes(row);
                return tip;
            }

            return null;
        }

        public FetchCoinsResponse FetchCoins(OutPoint[] utxos)
        {
            FetchCoinsResponse res = new FetchCoinsResponse();

            using (new StopwatchDisposable(o => this.performanceCounter.AddQueryTime(o)))
            {
                this.performanceCounter.AddQueriedEntities(utxos.Length);

                foreach (OutPoint outPoint in utxos)
                {
                    byte[] row = this.coinDb.Get(coinsTable, outPoint.ToBytes());
                    Coins outputs = row != null ? this.dBreezeSerializer.Deserialize<Coins>(row) : null;

                    this.logger.LogDebug("Outputs for '{0}' were {1}.", outPoint, outputs == null ? "NOT loaded" : "loaded");

                    res.UnspentOutputs.Add(outPoint, new UnspentOutput(outPoint, outputs));
                }
            }

            return res;
        }

        public void SaveChanges(IList<UnspentOutput> unspentOutputs, Dictionary<TxDestination, Dictionary<uint, long>> balanceUpdates, HashHeightPair oldBlockHash, HashHeightPair nextBlockHash, List<RewindData> rewindDataList = null)
        {
            int insertedEntities = 0;

            using (var batch = this.coinDb.GetReadWriteBatch(coinsTable, rewindTable, blockTable))
            {
                this.AdjustBalance(batch, balanceUpdates);

                using (new StopwatchDisposable(o => this.performanceCounter.AddInsertTime(o)))
                {
                    HashHeightPair current = this.GetTipHash();
                    if (current != oldBlockHash)
                    {
                        this.logger.LogTrace("(-)[BLOCKHASH_MISMATCH]");
                        throw new InvalidOperationException("Invalid oldBlockHash");
                    }

                    // Here we'll add items to be inserted in a second pass.
                    List<UnspentOutput> toInsert = new List<UnspentOutput>();

                    foreach (var coin in unspentOutputs.OrderBy(utxo => utxo.OutPoint, new OutPointComparer()))
                    {
                        if (coin.Coins == null)
                        {
                            this.logger.LogDebug("Outputs of transaction ID '{0}' are prunable and will be removed from the database.", coin.OutPoint);
                            batch.Delete(coinsTable, coin.OutPoint.ToBytes());
                        }
                        else
                        {
                            // Add the item to another list that will be used in the second pass.
                            // This is for performance reasons: dBreeze is optimized to run the same kind of operations, sorted.
                            toInsert.Add(coin);
                        }
                    }

                    for (int i = 0; i < toInsert.Count; i++)
                    {
                        var coin = toInsert[i];
                        this.logger.LogDebug("Outputs of transaction ID '{0}' are NOT PRUNABLE and will be inserted into the database. {1}/{2}.", coin.OutPoint, i, toInsert.Count);

                        batch.Put(coinsTable, coin.OutPoint.ToBytes(), this.dBreezeSerializer.Serialize(coin.Coins));
                    }

                    if (rewindDataList != null)
                    {
                        foreach (RewindData rewindData in rewindDataList)
                        {
                            var nextRewindIndex = rewindData.PreviousBlockHash.Height + 1;

                            this.logger.LogDebug("Rewind state #{0} created.", nextRewindIndex);

                            batch.Put(rewindTable, BitConverter.GetBytes(nextRewindIndex).Reverse().ToArray(), this.dBreezeSerializer.Serialize(rewindData));
                        }
                    }

                    insertedEntities += unspentOutputs.Count;
                    this.SetBlockHash(batch, nextBlockHash);
                    batch.Write();
                }
            }

            this.performanceCounter.AddInsertedEntities(insertedEntities);
        }

        /// <inheritdoc />
        public int GetMinRewindHeight()
        {
            // Find the first row with a rewind table key prefix.
            using (var iterator = this.coinDb.GetIterator(rewindTable))
            {
                iterator.SeekFirst();
                if (!iterator.IsValid())
                    return -1;

                byte[] key = iterator.Key();

                if (key.Length != 4)
                    return -1;

                return BitConverter.ToInt32(key.SafeSubarray(0, 4).Reverse().ToArray());
            }
        }

        private bool TryGetCoins(ReadWriteBatch readWriteBatch, byte[] key, out Coins coins)
        {
            byte[] row2 = readWriteBatch.Get(coinsTable, key);
            if (row2 == null)
            {
                coins = null;
                return false;
            }

            coins = this.dBreezeSerializer.Deserialize<Coins>(row2);

            return true;
        }

        public HashHeightPair Rewind(HashHeightPair target)
        {
            HashHeightPair current = this.GetTipHash();
            return RewindInternal(current.Height, target);
        }

        private HashHeightPair RewindInternal(int startHeight, HashHeightPair target)
        {
            HashHeightPair res = null;

            int indexedHeight = this.GetIndexedTipHash()?.Height ?? -1;

            using (var batch = this.coinDb.GetReadWriteBatch(coinsTable, rewindTable, blockTable))
            {
                var balanceAdjustments = new Dictionary<TxDestination, Dictionary<uint, long>>();

                for (int height = startHeight; height > (target?.Height ?? (startHeight - 1)) && height > (startHeight - MaxRewindBatchSize); height--)
                {
                    byte[] rowKey = BitConverter.GetBytes(height).Reverse().ToArray();
                    byte[] row = this.coinDb.Get(rewindTable, rowKey);

                    if (row == null)
                        throw new InvalidOperationException($"No rewind data found for block at height {height}.");

                    batch.Delete(rewindTable, rowKey);

                    var rewindData = this.dBreezeSerializer.Deserialize<RewindData>(row);

                    foreach (OutPoint outPoint in rewindData.OutputsToRemove)
                    {
                        byte[] key = outPoint.ToBytes();
                        if (this.TryGetCoins(batch, key, out Coins coins))
                        {
                            this.logger.LogDebug("Outputs of outpoint '{0}' will be removed.", outPoint);

                            if (height <= indexedHeight)
                                Update(balanceAdjustments, coins.TxOut.ScriptPubKey, coins.Height, -coins.TxOut.Value);

                            batch.Delete(coinsTable, key);
                        }
                        else
                        {
                            throw new InvalidOperationException(string.Format("Outputs of outpoint '{0}' were not found when attempting removal.", outPoint));
                        }
                    }

                    foreach (RewindDataOutput rewindDataOutput in rewindData.OutputsToRestore)
                    {
                        this.logger.LogDebug("Outputs of outpoint '{0}' will be restored.", rewindDataOutput.OutPoint);
                        batch.Put(coinsTable, rewindDataOutput.OutPoint.ToBytes(), this.dBreezeSerializer.Serialize(rewindDataOutput.Coins));

                        if (height <= indexedHeight)
                            Update(balanceAdjustments, rewindDataOutput.Coins.TxOut.ScriptPubKey, (uint)height, rewindDataOutput.Coins.TxOut.Value);
                    }

                    res = rewindData.PreviousBlockHash;
                }

                AdjustBalance(batch, balanceAdjustments);

                this.SetBlockHash(batch, res, res.Height < indexedHeight);
                batch.Write();
            }

            return res;
        }

        public RewindData GetRewindData(int height)
        {
            byte[] row = this.coinDb.Get(rewindTable, BitConverter.GetBytes(height).Reverse().ToArray());
            return row != null ? this.dBreezeSerializer.Deserialize<RewindData>(row) : null;
        }

        public void PutStake(IEnumerable<StakeItem> stakeEntries)
        {
            using (var batch = this.coinDb.GetWriteBatch())
            {
                foreach (StakeItem stakeEntry in stakeEntries)
                {
                    if (!stakeEntry.InStore)
                    {
                        batch.Put(stakeTable, stakeEntry.BlockId.ToBytes(false), this.dBreezeSerializer.Serialize(stakeEntry.BlockStake));
                        stakeEntry.InStore = true;
                    }
                }

                batch.Write();
            }
        }

        public void GetStake(IEnumerable<StakeItem> blocklist)
        {
            foreach (StakeItem blockStake in blocklist)
            {
                this.logger.LogTrace("Loading POS block hash '{0}' from the database.", blockStake.BlockId);
                byte[] stakeRow = this.coinDb.Get(stakeTable, blockStake.BlockId.ToBytes(false));

                if (stakeRow != null)
                {
                    blockStake.BlockStake = this.dBreezeSerializer.Deserialize<BlockStake>(stakeRow);
                    blockStake.InStore = true;
                }
            }
        }

        private void AddBenchStats(StringBuilder log)
        {
            log.AppendLine(">> Coindb Bench");

            BackendPerformanceSnapshot snapShot = this.performanceCounter.Snapshot();

            if (this.latestPerformanceSnapShot == null)
                log.AppendLine(snapShot.ToString());
            else
                log.AppendLine((snapShot - this.latestPerformanceSnapShot).ToString());

            this.latestPerformanceSnapShot = snapShot;
        }

        private void AdjustBalance(ReadWriteBatch batch, Dictionary<TxDestination, Dictionary<uint, long>> balanceUpdates)
        {
            foreach ((TxDestination txDestination, Dictionary<uint, long> balanceAdjustments) in balanceUpdates)
            {
                long totalAdjustment = 0;

                foreach (uint height in balanceAdjustments.Keys.OrderBy(k => k))
                {
                    var key = txDestination.ToBytes().Concat(BitConverter.GetBytes(height).Reverse()).ToArray();
                    byte[] row = batch.Get(balanceAdjustmentTable, key);
                    long adjustment = balanceAdjustments[height];
                    long balance = ((row == null) ? 0 : BitConverter.ToInt64(row)) + adjustment;
                    batch.Put(balanceAdjustmentTable, key, BitConverter.GetBytes(balance));

                    totalAdjustment += adjustment;
                }

                {
                    var key = txDestination.ToBytes();
                    byte[] row = batch.Get(balanceTable, key);
                    long balance = ((row == null) ? 0 : BitConverter.ToInt64(row)) + totalAdjustment;
                    batch.Put(balanceTable, key, BitConverter.GetBytes(balance));
                }
            }
        }

        private void Update(Dictionary<TxDestination, Dictionary<uint, long>> balanceAdjustments, Script scriptPubKey, uint height, long change)
        {
            if (scriptPubKey.Length == 0 || change == 0)
                return;

            foreach (TxDestination txDestination in this.scriptAddressReader.GetDestinationFromScriptPubKey(this.network, scriptPubKey))
            {
                if (!balanceAdjustments.TryGetValue(txDestination, out Dictionary<uint, long> value))
                {
                    value = new Dictionary<uint, long>();
                    balanceAdjustments[txDestination] = value;
                }

                if (!value.TryGetValue(height, out long balance))
                    balance = change;
                else
                    balance += change;

                value[height] = balance;
            }
        }

        /// <inheritdoc />
        public IEnumerable<(uint height, long satoshis)> GetBalance(TxDestination txDestination)
        {
            long balance;
            {
                byte[] row = this.coinDb.Get(balanceTable, txDestination.ToBytes());
                balance = (row == null) ? 0 : BitConverter.ToInt64(row);
            }

            using (var iterator = this.coinDb.GetIterator(balanceAdjustmentTable))
            {
                foreach ((uint height, long adjustment) in iterator.GetAll(ascending: false,
                    lastKey: txDestination.ToBytes().Concat(BitConverter.GetBytes(this.persistedCoinviewTip.Height + 1).Reverse()).ToArray(),
                    includeLastKey: false,
                    firstKey: txDestination.ToBytes(),
                    includeFirstKey: false).Select(x => (height: BitConverter.ToUInt32(x.Item1.Reverse().ToArray()), adjustment: BitConverter.ToInt64(x.Item2))))
                {
                    yield return (height, balance);
                    balance -= adjustment;
                }
            }

            yield return (0, balance);
        }

        /// <inheritdoc />
        public void Dispose()
        {
            this.coinDb.Dispose();
        }
    }
}