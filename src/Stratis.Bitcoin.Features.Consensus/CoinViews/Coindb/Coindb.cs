using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Microsoft.Extensions.Logging;
using NBitcoin;
using Stratis.Bitcoin.Configuration;
using Stratis.Bitcoin.Configuration.Logging;
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
        public const byte CoinsTable = 1;
        public const byte BlockTable = 2;
        public const byte RewindTable = 3;
        public const byte StakeTable = 4;
        public const byte BalanceTable = 5;
        public const byte BalanceAdjustmentTable = 6;

        /// <summary>Database key under which the block hash of the coin view's current tip is stored.</summary>
        private static readonly byte[] blockHashKey = new byte[0];

        /// <summary>Database key under which the block hash of the coin view's last indexed tip is stored.</summary>
        private static readonly byte[] blockIndexedHashKey = new byte[1];

        /// <summary>Instance logger.</summary>
        private ILogger logger;

        public bool BalanceIndexingEnabled { get; private set; }

        /// <summary>Access to dBreeze database.</summary>
        private IDb coinDb;

        /// <summary>Hash of the block which is currently the tip of the coinview.</summary>
        private HashHeightPair persistedCoinviewTip;

        private readonly IScriptAddressReader scriptAddressReader;

        private readonly string dataFolder;

        /// <summary>Specification of the network the node runs on - regtest/testnet/mainnet.</summary>
        private readonly Network network;

        /// <summary>Performance counter to measure performance of the database insert and query operations.</summary>
        private readonly BackendPerformanceCounter performanceCounter;

        private BackendPerformanceSnapshot latestPerformanceSnapShot;

        private readonly DBreezeSerializer dBreezeSerializer;

        private const int MaxRewindBatchSize = 10000;

        public Coindb(Network network, DataFolder dataFolder, IDateTimeProvider dateTimeProvider, INodeStats nodeStats, DBreezeSerializer dBreezeSerializer, IScriptAddressReader scriptAddressReader)
        {
            Guard.NotNull(network, nameof(network));
            Guard.NotNull(dataFolder, nameof(dataFolder));

            this.dataFolder = dataFolder.CoindbPath;
            this.dBreezeSerializer = dBreezeSerializer;
            this.logger = LogManager.GetCurrentClassLogger();
            this.network = network;
            this.performanceCounter = new BackendPerformanceCounter(dateTimeProvider);
            this.scriptAddressReader = scriptAddressReader;

            if (nodeStats.DisplayBenchStats)
                nodeStats.RegisterStats(this.AddBenchStats, StatsType.Benchmark, this.GetType().Name, 400);
        }

        /// <inheritdoc />
        public IEnumerable<(uint height, long satoshis)> GetBalance(TxDestination txDestination)
        {
            long balance;
            {
                byte[] row = this.coinDb.Get(BalanceTable, txDestination.ToBytes());
                balance = (row == null) ? 0 : BitConverter.ToInt64(row);
            }

            foreach ((byte[] key, byte[] value) in this.coinDb.GetAll(BalanceAdjustmentTable, ascending: false,
                lastKey: txDestination.ToBytes().Concat(BitConverter.GetBytes(this.persistedCoinviewTip.Height + 1).Reverse()).ToArray(),
                includeLastKey: false,
                firstKey: txDestination.ToBytes(),
                includeFirstKey: false))
            {
                yield return (BitConverter.ToUInt32(key.Reverse().ToArray()), balance);
                balance -= BitConverter.ToInt64(value);
            }

            yield return (0, balance);
        }

        /// <inheritdoc />
        public void Initialize(ChainedHeader chainTip, bool balanceIndexingEnabled)
        {
            // Open a connection to a new DB and create if not found
            this.coinDb = new T();
            this.coinDb.Open(this.dataFolder);

            this.BalanceIndexingEnabled = balanceIndexingEnabled;

            EnsureCoinDatabaseIntegrity(chainTip);

            Block genesis = this.network.GetGenesis();

            if (this.GetTipHash() == null)
            {
                using (var batch = this.coinDb.GetWriteBatch())
                {
                    this.SetBlockHash(batch, new HashHeightPair(genesis.GetHash(), 0));
                    batch.Write();
                }
            }

            this.logger.LogInformation("Coinview initialized with tip '{0}'.", this.persistedCoinviewTip);
        }

        /// <inheritdoc />
        public FetchCoinsResponse FetchCoins(OutPoint[] utxos)
        {
            FetchCoinsResponse res = new FetchCoinsResponse();

            using (new StopwatchDisposable(o => this.performanceCounter.AddQueryTime(o)))
            {
                this.performanceCounter.AddQueriedEntities(utxos.Length);

                foreach (OutPoint outPoint in utxos)
                {
                    byte[] row = this.coinDb.Get(CoinsTable, outPoint.ToBytes());
                    Coins outputs = row != null ? this.dBreezeSerializer.Deserialize<Coins>(row) : null;

                    this.logger.LogDebug("Outputs for '{0}' were {1}.", outPoint, outputs == null ? "NOT loaded" : "loaded");

                    res.UnspentOutputs.Add(outPoint, new UnspentOutput(outPoint, outputs));
                }
            }

            return res;
        }

        /// <inheritdoc />
        public void SaveChanges(IList<UnspentOutput> unspentOutputs, Dictionary<TxDestination, Dictionary<uint, long>> balanceUpdates, HashHeightPair oldBlockHash, HashHeightPair nextBlockHash, List<RewindData> rewindDataList = null)
        {
            int insertedEntities = 0;

            using (var batch = this.coinDb.GetWriteBatch())
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
                            batch.Delete(CoinsTable, coin.OutPoint.ToBytes());
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

                        batch.Put(CoinsTable, coin.OutPoint.ToBytes(), this.dBreezeSerializer.Serialize(coin.Coins));
                    }

                    if (rewindDataList != null)
                    {
                        foreach (RewindData rewindData in rewindDataList)
                        {
                            var nextRewindIndex = rewindData.PreviousBlockHash.Height + 1;

                            this.logger.LogDebug("Rewind state #{0} created.", nextRewindIndex);

                            batch.Put(RewindTable, BitConverter.GetBytes(nextRewindIndex).Reverse().ToArray(), this.dBreezeSerializer.Serialize(rewindData));
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
            var res = this.coinDb.GetAll(RewindTable, keysOnly: true, firstKey: new byte[] { }).FirstOrDefault();
            if (res == default || res.Item1.Length != 5)
                return -1;

            return BitConverter.ToInt32(res.Item1.SafeSubarray(0, 4).Reverse().ToArray());
        }

        /// <inheritdoc />
        public HashHeightPair Rewind(HashHeightPair target)
        {
            HashHeightPair current = this.GetTipHash();
            return RewindInternal(current.Height, target);
        }

        /// <inheritdoc />
        public RewindData GetRewindData(int height)
        {
            byte[] row = this.coinDb.Get(RewindTable, BitConverter.GetBytes(height).Reverse().ToArray());
            return row != null ? this.dBreezeSerializer.Deserialize<RewindData>(row) : null;
        }

        /// <inheritdoc />
        public void PutStake(IEnumerable<StakeItem> stakeEntries)
        {
            using (var batch = this.coinDb.GetWriteBatch())
            {
                foreach (StakeItem stakeEntry in stakeEntries)
                {
                    if (!stakeEntry.InStore)
                    {
                        batch.Put(StakeTable, stakeEntry.BlockId.ToBytes(false), this.dBreezeSerializer.Serialize(stakeEntry.BlockStake));
                        stakeEntry.InStore = true;
                    }
                }

                batch.Write();
            }
        }

        /// <inheritdoc />
        public void GetStake(IEnumerable<StakeItem> blocklist)
        {
            foreach (StakeItem blockStake in blocklist)
            {
                this.logger.LogTrace("Loading POS block hash '{0}' from the database.", blockStake.BlockId);
                byte[] stakeRow = this.coinDb.Get(StakeTable, blockStake.BlockId.ToBytes(false));

                if (stakeRow != null)
                {
                    blockStake.BlockStake = this.dBreezeSerializer.Deserialize<BlockStake>(stakeRow);
                    blockStake.InStore = true;
                }
            }
        }

        /// <inheritdoc />
        public HashHeightPair GetTipHash()
        {
            if (this.persistedCoinviewTip == null)
            {
                var row = this.coinDb.Get(BlockTable, blockHashKey);
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
            var row = this.coinDb.Get(BlockTable, blockIndexedHashKey);
            if (row != null)
            {
                var tip = new HashHeightPair();
                tip.FromBytes(row);
                return tip;
            }

            return null;
        }

        private bool TryGetCoins(byte[] key, out Coins coins)
        {
            byte[] row2 = this.coinDb.Get(CoinsTable, key);
            if (row2 == null)
            {
                coins = null;
                return false;
            }

            coins = this.dBreezeSerializer.Deserialize<Coins>(row2);

            return true;
        }

        private HashHeightPair RewindInternal(int startHeight, HashHeightPair target)
        {
            HashHeightPair res = null;

            int indexedHeight = this.GetIndexedTipHash()?.Height ?? -1;

            using (var batch = this.coinDb.GetWriteBatch())
            {
                var balanceAdjustments = new Dictionary<TxDestination, Dictionary<uint, long>>();

                for (int height = startHeight; height > (target?.Height ?? (startHeight - 1)) && height > (startHeight - MaxRewindBatchSize); height--)
                {
                    byte[] rowKey = BitConverter.GetBytes(height).Reverse().ToArray();
                    byte[] row = this.coinDb.Get(RewindTable, rowKey);

                    if (row == null)
                        throw new InvalidOperationException($"No rewind data found for block at height {height}.");

                    batch.Delete(RewindTable, rowKey);

                    var rewindData = this.dBreezeSerializer.Deserialize<RewindData>(row);

                    foreach (OutPoint outPoint in rewindData.OutputsToRemove)
                    {
                        byte[] key = outPoint.ToBytes();
                        if (this.TryGetCoins(key, out Coins coins))
                        {
                            this.logger.LogDebug("Outputs of outpoint '{0}' will be removed.", outPoint);

                            if (height <= indexedHeight)
                                Update(balanceAdjustments, coins.TxOut.ScriptPubKey, coins.Height, -coins.TxOut.Value);

                            batch.Delete(CoinsTable, key);
                        }
                        else
                        {
                            throw new InvalidOperationException(string.Format("Outputs of outpoint '{0}' were not found when attempting removal.", outPoint));
                        }
                    }

                    foreach (RewindDataOutput rewindDataOutput in rewindData.OutputsToRestore)
                    {
                        this.logger.LogDebug("Outputs of outpoint '{0}' will be restored.", rewindDataOutput.OutPoint);
                        batch.Put(CoinsTable, rewindDataOutput.OutPoint.ToBytes(), this.dBreezeSerializer.Serialize(rewindDataOutput.Coins));

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


        private void SetBlockHash(IDbBatch batch, HashHeightPair nextBlockHash, bool forceUpdateIndexedHeight = false)
        {
            this.persistedCoinviewTip = nextBlockHash;
            batch.Put(BlockTable, blockHashKey, nextBlockHash.ToBytes());
            if (this.BalanceIndexingEnabled || forceUpdateIndexedHeight)
                batch.Put(BlockTable, blockIndexedHashKey, nextBlockHash.ToBytes());
        }

        private void EnsureCoinDatabaseIntegrity(ChainedHeader chainTip)
        {
            this.logger.LogInformation("Checking coin database integrity...");

            HashHeightPair maxHeight = new HashHeightPair(chainTip);

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

                if (indexedTipHash.Height < chainTip.Height)
                {
                    this.logger.LogInformation($"Rewinding the coin database to include missing balance information.");
                    maxHeight = indexedTipHash;
                }
            }

            var heightToCheck = chainTip.Height;

            // Find the height up to where rewind data is stored above chain tip.
            do
            {
                heightToCheck += 1;

                byte[] row = this.coinDb.Get(RewindTable, BitConverter.GetBytes(heightToCheck).Reverse().ToArray());
                if (row == null)
                    break;
            } while (true);

            for (int height = heightToCheck - 1; height > maxHeight.Height;)
            {
                this.logger.LogInformation($"Fixing coin database, deleting rewind data at height {height} above tip '{chainTip}'.");

                // Do a batch of rewinding.
                height = RewindInternal(height, maxHeight).Height;
            }

            this.logger.LogInformation("Coin database integrity good.");
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


        private void AdjustBalance(IDbBatch batch, Dictionary<TxDestination, Dictionary<uint, long>> balanceUpdates)
        {
            foreach ((TxDestination txDestination, Dictionary<uint, long> balanceAdjustments) in balanceUpdates)
            {
                long totalAdjustment = 0;

                foreach (uint height in balanceAdjustments.Keys.OrderBy(k => k))
                {
                    var key = txDestination.ToBytes().Concat(BitConverter.GetBytes(height).Reverse()).ToArray();
                    byte[] row = this.coinDb.Get(BalanceAdjustmentTable, key);
                    long balance = ((row == null) ? 0 : BitConverter.ToInt64(row)) + balanceAdjustments[height];
                    batch.Put(BalanceAdjustmentTable, key, BitConverter.GetBytes(balance));

                    totalAdjustment += balance;
                }

                {
                    var key = txDestination.ToBytes();
                    byte[] row = this.coinDb.Get(BalanceTable, key);
                    long balance = ((row == null) ? 0 : BitConverter.ToInt64(row)) + totalAdjustment;
                    batch.Put(BalanceTable, key, BitConverter.GetBytes(balance));
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
        public void Dispose()
        {
            this.coinDb.Dispose();
        }
    }
}
