using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Microsoft.Extensions.Logging;
using NBitcoin;
using RocksDbSharp;
using Stratis.Bitcoin.Configuration;
using Stratis.Bitcoin.Configuration.Logging;
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

        private readonly string dataFolder;

        /// <summary>Hash of the block which is currently the tip of the coinview.</summary>
        private HashHeightPair persistedCoinviewTip;
        private readonly DBreezeSerializer dBreezeSerializer;
        private DbOptions dbOptions;
        private RocksDb rocksDb;
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
            this.dataFolder = dataFolder.CoindbPath;
            this.dBreezeSerializer = dBreezeSerializer;
            this.logger = LogManager.GetCurrentClassLogger();
            this.network = network;
            this.performanceCounter = new BackendPerformanceCounter(dateTimeProvider);

            if (nodeStats.DisplayBenchStats)
                nodeStats.RegisterStats(this.AddBenchStats, StatsType.Benchmark, this.GetType().Name, 400);
        }

        public void Initialize(ChainedHeader chainTip)
        {
            this.dbOptions = new DbOptions().SetCreateIfMissing(true);
            this.rocksDb = RocksDb.Open(this.dbOptions, this.dataFolder);

            // Check if key bytes are in the wrong endian order.
            HashHeightPair current = this.GetTipHash();
            if (current != null)
            {
                byte[] row = this.rocksDb.Get(new byte[] { rewindTable }.Concat(BitConverter.GetBytes(current.Height)).ToArray());

                // Fix the table if required.
                if (row != null)
                {
                    // To be sure, check the next height too.
                    byte[] row2 = (current.Height > 1) ? this.rocksDb.Get(new byte[] { rewindTable }.Concat(BitConverter.GetBytes(current.Height - 1)).ToArray()) : new byte[] { };
                    if (row2 != null)
                    {
                        this.logger.LogInformation("Fixing the coin db.");

                        var rows = new Dictionary<int, byte[]>();

                        using (var iterator = this.rocksDb.NewIterator())
                        {
                            iterator.Seek(new byte[] { rewindTable });

                            while (iterator.Valid())
                            {
                                byte[] key = iterator.Key();

                                if (key.Length != 5 || key[0] != rewindTable)
                                    break;

                                int height = BitConverter.ToInt32(key, 1);

                                rows[height] = iterator.Value();

                                iterator.Next();
                            }
                        }

                        using (var batch = new WriteBatch())
                        {
                            foreach (int height in rows.Keys.OrderBy(k => k))
                            {
                                batch.Delete(new byte[] { rewindTable }.Concat(BitConverter.GetBytes(height)).ToArray());
                            }

                            foreach (int height in rows.Keys.OrderBy(k => k))
                            {
                                batch.Put(new byte[] { rewindTable }.Concat(BitConverter.GetBytes(height).Reverse()).ToArray(), rows[height]);
                            }

                            this.rocksDb.Write(batch);
                        }
                    }
                }
            }

            EnsureCoinDatabaseIntegrity(chainTip);

            Block genesis = this.network.GetGenesis();

            if (this.GetTipHash() == null)
                this.SetBlockHash(new HashHeightPair(genesis.GetHash(), 0));

            this.logger.LogInformation("Coinview initialized with tip '{0}'.", this.persistedCoinviewTip);
        }

        private void EnsureCoinDatabaseIntegrity(ChainedHeader chainTip)
        {
            this.logger.LogInformation("Checking coin database integrity...");

            var heightToCheck = chainTip.Height;

            // Find the height up to where rewind data is stored above chain tip.
            do
            {
                heightToCheck += 1;

                byte[] row = this.rocksDb.Get(new byte[] { rewindTable }.Concat(BitConverter.GetBytes(heightToCheck).Reverse()).ToArray());
                if (row == null)
                    break;

            } while (true);

            using (var batch = new WriteBatch())
            {
                for (int height = heightToCheck - 1; height > chainTip.Height; height--)
                {
                    this.logger.LogInformation($"Fixing coin database, deleting rewind data at height {height} above tip '{chainTip}'.");
                    RewindInternal(batch, height);
                }
            }

            this.logger.LogInformation("Coin database integrity good.");
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

                    this.logger.LogDebug("Outputs for '{0}' were {1}.", outPoint, outputs == null ? "NOT loaded" : "loaded");

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
                        this.logger.LogError("(-)[BLOCKHASH_MISMATCH]");
                        throw new InvalidOperationException("Invalid oldBlockHash");
                    }

                    // Here we'll add items to be inserted in a second pass.
                    List<UnspentOutput> toInsert = new List<UnspentOutput>();

                    foreach (var coin in unspentOutputs.OrderBy(utxo => utxo.OutPoint, new OutPointComparer()))
                    {
                        if (coin.Coins == null)
                        {
                            this.logger.LogDebug("Outputs of transaction ID '{0}' are prunable and will be removed from the database.", coin.OutPoint);
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
                        this.logger.LogDebug("Outputs of transaction ID '{0}' are NOT PRUNABLE and will be inserted into the database. {1}/{2}.", coin.OutPoint, i, toInsert.Count);

                        batch.Put(new byte[] { coinsTable }.Concat(coin.OutPoint.ToBytes()).ToArray(), this.dBreezeSerializer.Serialize(coin.Coins));
                    }

                    if (rewindDataList != null)
                    {
                        foreach (RewindData rewindData in rewindDataList)
                        {
                            var nextRewindIndex = rewindData.PreviousBlockHash.Height + 1;

                            this.logger.LogDebug("Rewind state #{0} created.", nextRewindIndex);

                            batch.Put(new byte[] { rewindTable }.Concat(BitConverter.GetBytes(nextRewindIndex).Reverse()).ToArray(), this.dBreezeSerializer.Serialize(rewindData));
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
        public int GetMinRewindHeight()
        {
            // Find the first row with a rewind table key prefix.
            using (var iterator = this.rocksDb.NewIterator())
            {
                iterator.Seek(new byte[] { rewindTable });
                if (!iterator.Valid())
                    return -1;

                byte[] key = iterator.Key();

                if (key.Length != 5 || key[0] != rewindTable)
                    return -1;

                return BitConverter.ToInt32(key.SafeSubarray(1, 4).Reverse().ToArray());
            }
        }

        /// <inheritdoc />
        public HashHeightPair Rewind()
        {
            using (var batch = new WriteBatch())
            {
                HashHeightPair current = this.GetTipHash();
                return RewindInternal(batch, current.Height);
            }
        }

        private HashHeightPair RewindInternal(WriteBatch batch, int height)
        {
            byte[] row = this.rocksDb.Get(new byte[] { rewindTable }.Concat(BitConverter.GetBytes(height).Reverse()).ToArray());

            if (row == null)
                throw new InvalidOperationException($"No rewind data found for block at height {height}.");

            batch.Delete(BitConverter.GetBytes(height));

            var rewindData = this.dBreezeSerializer.Deserialize<RewindData>(row);

            foreach (OutPoint outPoint in rewindData.OutputsToRemove)
            {
                this.logger.LogDebug("Outputs of outpoint '{0}' will be removed.", outPoint);
                batch.Delete(new byte[] { coinsTable }.Concat(outPoint.ToBytes()).ToArray());
            }

            foreach (RewindDataOutput rewindDataOutput in rewindData.OutputsToRestore)
            {
                this.logger.LogDebug("Outputs of outpoint '{0}' will be restored.", rewindDataOutput.OutPoint);
                batch.Put(new byte[] { coinsTable }.Concat(rewindDataOutput.OutPoint.ToBytes()).ToArray(), this.dBreezeSerializer.Serialize(rewindDataOutput.Coins));
            }

            this.rocksDb.Write(batch);

            this.SetBlockHash(rewindData.PreviousBlockHash);

            return rewindData.PreviousBlockHash;
        }

        public RewindData GetRewindData(int height)
        {
            byte[] row = this.rocksDb.Get(new byte[] { rewindTable }.Concat(BitConverter.GetBytes(height).Reverse()).ToArray());
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
                this.logger.LogDebug("Loading POS block hash '{0}' from the database.", blockStake.BlockId);
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
            this.rocksDb.Dispose();
        }
    }
}