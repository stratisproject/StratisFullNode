using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using NBitcoin;
using NLog;
using RocksDbSharp;
using Stratis.Bitcoin.Configuration;
using Stratis.Bitcoin.Utilities;

namespace Stratis.Bitcoin.Features.Consensus.CoinViews
{
    /// <summary>
    /// Persistent implementation of coinview using dBreeze database.
    /// </summary>
    public class RocksDbCoindb : ICoindb, IStakedb, IDisposable
    {
        /// <summary>Database key under which the block hash of the coin view's current tip is stored.</summary>
        private static readonly byte[] blockHashKey = new byte[0];

        private static readonly byte coinsTable = 1;
        private static readonly byte blockTable = 2;
        private static readonly byte rewindTable = 3;
        private static readonly byte stakeTable = 4;

        /// <summary>Hash of the block which is currently the tip of the coinview.</summary>
        private HashHeightPair persistedCoinviewTip;
        private readonly DBreezeSerializer dBreezeSerializer;
        private readonly DbOptions dbOptions;
        private readonly RocksDb rocksDb;
        private BackendPerformanceSnapshot latestPerformanceSnapShot;
        private readonly ILogger logger;
        private readonly Network network;
        private readonly BackendPerformanceCounter performanceCounter;

        public RocksDbCoindb(
            Network network,
            DataFolder dataFolder,
            IDateTimeProvider dateTimeProvider,
            INodeStats nodeStats,
            DBreezeSerializer dBreezeSerializer)
        {
            this.dbOptions = new DbOptions().SetCreateIfMissing(true);
            this.rocksDb = RocksDb.Open(this.dbOptions, dataFolder.CoindbPath);
            this.dBreezeSerializer = dBreezeSerializer;
            this.logger = LogManager.GetCurrentClassLogger();
            this.network = network;
            this.performanceCounter = new BackendPerformanceCounter(dateTimeProvider);

            if (nodeStats.DisplayBenchStats)
                nodeStats.RegisterStats(this.AddBenchStats, StatsType.Benchmark, this.GetType().Name, 400);
        }

        public void Initialize()
        {
            Block genesis = this.network.GetGenesis();

            if (this.GetTipHash() == null)
                this.SetBlockHash(new HashHeightPair(genesis.GetHash(), 0));

            this.logger.Info("Coinview initialized with tip '{0}'.", this.persistedCoinviewTip);
        }

        private void SetBlockHash(HashHeightPair nextBlockHash)
        {
            this.persistedCoinviewTip = nextBlockHash;
            this.rocksDb.Put(new byte[] { blockTable }.Concat(blockHashKey).ToArray(), nextBlockHash.ToBytes());
        }

        public HashHeightPair GetTipHash()
        {
            if (this.persistedCoinviewTip == null)
            {
                var row = this.rocksDb.Get(new byte[] { blockTable }.Concat(blockHashKey).ToArray());
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
                    byte[] row = this.rocksDb.Get(new byte[] { coinsTable }.Concat(outPoint.ToBytes()).ToArray());
                    Coins outputs = row != null ? this.dBreezeSerializer.Deserialize<Coins>(row) : null;

                    this.logger.Debug("Outputs for '{0}' were {1}.", outPoint, outputs == null ? "NOT loaded" : "loaded");

                    res.UnspentOutputs.Add(outPoint, new UnspentOutput(outPoint, outputs));
                }
            }

            return res;
        }

        public void SaveChanges(IList<UnspentOutput> unspentOutputs, HashHeightPair oldBlockHash, HashHeightPair nextBlockHash, List<RewindData> rewindDataList = null)
        {
            int insertedEntities = 0;

            using (var batch = new WriteBatch())
            {
                using (new StopwatchDisposable(o => this.performanceCounter.AddInsertTime(o)))
                {
                    HashHeightPair current = this.GetTipHash();
                    if (current != oldBlockHash)
                    {
                        this.logger.Error("(-)[BLOCKHASH_MISMATCH]");
                        throw new InvalidOperationException("Invalid oldBlockHash");
                    }

                    // Here we'll add items to be inserted in a second pass.
                    List<UnspentOutput> toInsert = new List<UnspentOutput>();

                    foreach (var coin in unspentOutputs.OrderBy(utxo => utxo.OutPoint, new OutPointComparer()))
                    {
                        if (coin.Coins == null)
                        {
                            this.logger.Debug("Outputs of transaction ID '{0}' are prunable and will be removed from the database.", coin.OutPoint);
                            batch.Delete(new byte[] { coinsTable }.Concat(coin.OutPoint.ToBytes()).ToArray());
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
                        this.logger.Debug("Outputs of transaction ID '{0}' are NOT PRUNABLE and will be inserted into the database. {1}/{2}.", coin.OutPoint, i, toInsert.Count);

                        batch.Put(new byte[] { coinsTable }.Concat(coin.OutPoint.ToBytes()).ToArray(), this.dBreezeSerializer.Serialize(coin.Coins));
                    }

                    if (rewindDataList != null)
                    {
                        foreach (RewindData rewindData in rewindDataList)
                        {
                            var nextRewindIndex = rewindData.PreviousBlockHash.Height + 1;

                            this.logger.Debug("Rewind state #{0} created.", nextRewindIndex);

                            batch.Put(new byte[] { rewindTable }.Concat(BitConverter.GetBytes(nextRewindIndex)).ToArray(), this.dBreezeSerializer.Serialize(rewindData));
                        }
                    }

                    insertedEntities += unspentOutputs.Count;
                    this.rocksDb.Write(batch);

                    this.SetBlockHash(nextBlockHash);
                }
            }

            this.performanceCounter.AddInsertedEntities(insertedEntities);
        }

        /// <inheritdoc />
        public HashHeightPair Rewind()
        {
            HashHeightPair previousBlockHash = null;
            using (var batch = new WriteBatch())
            {
                HashHeightPair current = this.GetTipHash();

                byte[] row = this.rocksDb.Get(new byte[] { rewindTable }.Concat(BitConverter.GetBytes(current.Height)).ToArray());

                if (row == null)
                    throw new InvalidOperationException($"No rewind data found for block `{current}`");

                batch.Delete(BitConverter.GetBytes(current.Height));

                var rewindData = this.dBreezeSerializer.Deserialize<RewindData>(row);

                foreach (OutPoint outPoint in rewindData.OutputsToRemove)
                {
                    this.logger.Debug("Outputs of outpoint '{0}' will be removed.", outPoint);
                    batch.Delete(new byte[] { coinsTable }.Concat(outPoint.ToBytes()).ToArray());
                }

                foreach (RewindDataOutput rewindDataOutput in rewindData.OutputsToRestore)
                {
                    this.logger.Debug("Outputs of outpoint '{0}' will be restored.", rewindDataOutput.OutPoint);
                    batch.Put(new byte[] { coinsTable }.Concat(rewindDataOutput.OutPoint.ToBytes()).ToArray(), this.dBreezeSerializer.Serialize(rewindDataOutput.Coins));
                }

                previousBlockHash = rewindData.PreviousBlockHash;

                this.rocksDb.Write(batch);

                this.SetBlockHash(rewindData.PreviousBlockHash);
            }

            return previousBlockHash;
        }

        public RewindData GetRewindData(int height)
        {
            byte[] row = this.rocksDb.Get(new byte[] { rewindTable }.Concat(BitConverter.GetBytes(height)).ToArray());
            return row != null ? this.dBreezeSerializer.Deserialize<RewindData>(row) : null;
        }

        /// <summary>
        /// Persists unsaved POS blocks information to the database.
        /// </summary>
        /// <param name="stakeEntries">List of POS block information to be examined and persists if unsaved.</param>
        public void PutStake(IEnumerable<StakeItem> stakeEntries)
        {
            using var batch = new WriteBatch();
            {
                foreach (StakeItem stakeEntry in stakeEntries)
                {
                    if (!stakeEntry.InStore)
                    {
                        batch.Put(new byte[] { stakeTable }.Concat(stakeEntry.BlockId.ToBytes(false)).ToArray(), this.dBreezeSerializer.Serialize(stakeEntry.BlockStake));
                        stakeEntry.InStore = true;
                    }
                }

                this.rocksDb.Write(batch);
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
                this.logger.Debug("Loading POS block hash '{0}' from the database.", blockStake.BlockId);
                byte[] stakeRow = this.rocksDb.Get(new byte[] { stakeTable }.Concat(blockStake.BlockId.ToBytes(false)).ToArray());

                if (stakeRow != null)
                {
                    blockStake.BlockStake = this.dBreezeSerializer.Deserialize<BlockStake>(stakeRow);
                    blockStake.InStore = true;
                }
            }
        }

        private void AddBenchStats(StringBuilder log)
        {
            log.AppendLine("======RocksDb Bench======");

            BackendPerformanceSnapshot snapShot = this.performanceCounter.Snapshot();

            if (this.latestPerformanceSnapShot == null)
                log.AppendLine(snapShot.ToString());
            else
                log.AppendLine((snapShot - this.latestPerformanceSnapShot).ToString());

            this.latestPerformanceSnapShot = snapShot;
        }

        public void Dispose()
        {
        }
    }
}