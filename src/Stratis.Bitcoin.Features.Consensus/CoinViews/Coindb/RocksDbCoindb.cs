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
    /// Persistent implementation of coinview using the RocksDb database engine.
    /// </summary>
    public class RocksDbCoindb : BaseCoindb, ICoindb, IStakedb, IDisposable
    {
        /// <summary>Database key under which the block hash of the coin view's current tip is stored.</summary>
        private static readonly byte[] blockHashKey = new byte[0];

        private readonly string dataFolder;

        /// <summary>Instance logger.</summary>
        private readonly ILogger logger;

        /// <summary>Specification of the network the node runs on - regtest/testnet/mainnet.</summary>
        private readonly Network network;

        /// <summary>Performance counter to measure performance of the database insert and query operations.</summary>
        private readonly BackendPerformanceCounter performanceCounter;

        private BackendPerformanceSnapshot latestPerformanceSnapShot;

        private readonly DBreezeSerializer dBreezeSerializer;

        private const int MaxRewindBatchSize = 10000;

        public bool BalanceIndexingEnabled { get; private set; }

        public RocksDbCoindb(Network network, DataFolder dataFolder, IDateTimeProvider dateTimeProvider,
            INodeStats nodeStats, DBreezeSerializer dBreezeSerializer, IScriptAddressReader scriptAddressReader)
            : this(network, dataFolder.CoindbPath, dateTimeProvider, nodeStats, dBreezeSerializer, scriptAddressReader)
        {
        }

        public RocksDbCoindb(Network network, string dataFolder, IDateTimeProvider dateTimeProvider,
            INodeStats nodeStats, DBreezeSerializer dBreezeSerializer, IScriptAddressReader scriptAddressReader) : base(network, scriptAddressReader)
        {
            Guard.NotNull(network, nameof(network));
            Guard.NotEmpty(dataFolder, nameof(dataFolder));

            this.dataFolder = dataFolder;
            this.dBreezeSerializer = dBreezeSerializer;
            this.logger = LogManager.GetCurrentClassLogger();
            this.network = network;
            this.performanceCounter = new BackendPerformanceCounter(dateTimeProvider);

            if (nodeStats.DisplayBenchStats)
                nodeStats.RegisterStats(this.AddBenchStats, StatsType.Benchmark, this.GetType().Name, 400);
        }

        public void Initialize(ChainedHeader chainTip, bool balanceIndexingEnabled)
        {
            // Open a connection to a new DB and create if not found
            this.coinDb = new RocksDb(this.dataFolder);
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

        private void EnsureCoinDatabaseIntegrity(ChainedHeader chainTip)
        {
            this.logger.LogInformation("Checking coin database integrity...");

            // If the balance table is empty then rebuild the coin db.
            if (this.BalanceIndexingEnabled && !this.coinDb.GetAll(balanceTable).Any())
            {
                this.logger.LogInformation($"Rebuilding coin database to include balance information.");
                this.coinDb.Clear();
                return;
            }

            var heightToCheck = chainTip.Height;

            // Find the height up to where rewind data is stored above chain tip.
            do
            {
                heightToCheck += 1;

                byte[] row = this.coinDb.Get(rewindTable, BitConverter.GetBytes(heightToCheck).Reverse().ToArray());
                if (row == null)
                    break;

            } while (true);

            for (int height = heightToCheck - 1; height > chainTip.Height;)
            {
                this.logger.LogInformation($"Fixing coin database, deleting rewind data at height {height} above tip '{chainTip}'.");

                // Do a batch of rewinding.
                height = RewindInternal(height, new HashHeightPair(chainTip)).Height;
            }

            this.logger.LogInformation("Coin database integrity good.");
        }

        private void SetBlockHash(IDbBatch batch, HashHeightPair nextBlockHash)
        {
            this.persistedCoinviewTip = nextBlockHash;
            batch.Put(blockTable, blockHashKey, nextBlockHash.ToBytes());
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

            using (var batch = this.coinDb.GetWriteBatch())
            {
                if (this.BalanceIndexingEnabled)
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

        public int GetMinRewindHeight()
        {
            // Find the first row with a rewind table key prefix.
            var res = this.coinDb.GetAll(rewindTable, keysOnly: true, firstKey: new byte[] { }).FirstOrDefault();
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

        private bool TryGetCoins(byte[] key, out Coins coins)
        {
            byte[] row2 = this.coinDb.Get(coinsTable, key);
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

            using (var batch = this.coinDb.GetWriteBatch())
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
                        if (this.TryGetCoins(key, out Coins coins))
                        {
                            this.logger.LogDebug("Outputs of outpoint '{0}' will be removed.", outPoint);

                            if (this.BalanceIndexingEnabled)
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

                        if (this.BalanceIndexingEnabled)
                            Update(balanceAdjustments, rewindDataOutput.Coins.TxOut.ScriptPubKey, (uint)height, rewindDataOutput.Coins.TxOut.Value);
                    }

                    res = rewindData.PreviousBlockHash;
                }

                AdjustBalance(batch, balanceAdjustments);

                this.SetBlockHash(batch, res);
                batch.Write();
            }

            return res;
        }

        public RewindData GetRewindData(int height)
        {
            byte[] row = this.coinDb.Get(rewindTable, BitConverter.GetBytes(height).Reverse().ToArray());
            return row != null ? this.dBreezeSerializer.Deserialize<RewindData>(row) : null;
        }

        /// <summary>
        /// Persists unsaved POS blocks information to the database.
        /// </summary>
        /// <param name="stakeEntries">List of POS block information to be examined and persists if unsaved.</param>
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

        /// <summary>
        /// Retrieves POS blocks information from the database.
        /// </summary>
        /// <param name="blocklist">List of partially initialized POS block information that is to be fully initialized with the values from the database.</param>
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
            log.AppendLine(">> RocksDb Bench");

            BackendPerformanceSnapshot snapShot = this.performanceCounter.Snapshot();

            if (this.latestPerformanceSnapShot == null)
                log.AppendLine(snapShot.ToString());
            else
                log.AppendLine((snapShot - this.latestPerformanceSnapShot).ToString());

            this.latestPerformanceSnapShot = snapShot;
        }

        /// <inheritdoc />
        public void Dispose()
        {
            this.coinDb.Dispose();
        }
    }
}