﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using Microsoft.Extensions.Logging;
using NBitcoin;
using Stratis.Bitcoin.Configuration;
using Stratis.Bitcoin.Configuration.Settings;
using Stratis.Bitcoin.Consensus;
using Stratis.Bitcoin.Features.Consensus.ProvenBlockHeaders;
using Stratis.Bitcoin.Features.Consensus.Rules.CommonRules;
using Stratis.Bitcoin.Interfaces;
using Stratis.Bitcoin.Primitives;
using Stratis.Bitcoin.Utilities;
using Stratis.Bitcoin.Utilities.Extensions;
using TracerAttributes;

namespace Stratis.Bitcoin.Features.Consensus.CoinViews
{
    /// <summary>
    /// Cache layer for coinview prevents too frequent updates of the data in the underlying storage.
    /// </summary>
    public class CachedCoinView : ICoinView
    {
        /// <summary>
        /// Item of the coinview cache that holds information about the unspent outputs
        /// as well as the status of the item in relation to the underlying storage.
        /// </summary>
        private class CacheItem
        {
            public OutPoint OutPoint;

            /// <summary>Information about transaction's outputs. Spent outputs are nulled.</summary>
            public Coins Coins;

            /// <summary><c>true</c> if the unspent output information is stored in the underlying storage, <c>false</c> otherwise.</summary>
            public bool ExistInInner;

            /// <summary><c>true</c> if the information in the cache is different than the information in the underlying storage.</summary>
            public bool IsDirty;

            public long GetSize
            {
                get
                {
                    // The fixed output size plus script size if present
                    return 32 + 4 + (this.Coins?.TxOut.ScriptPubKey.Length ?? 0);
                }
            }

            public long GetScriptSize
            {
                get
                {
                    // Script size if present
                    return this.Coins?.TxOut.ScriptPubKey.Length ?? 0;
                }
            }
        }

        /// <summary>
        /// Length of the coinview cache flushing interval in seconds, in case of a crash up to that number of seconds of syncing blocks are lost.
        /// </summary>
        /// <remarks>
        /// The longer the time interval the better performant the coinview will be,
        /// UTXOs that are added and deleted before they are flushed never reach the underline disk
        /// this saves 3 operations to disk (write the coinview and later read and delete it).
        /// However if this interval is too high the cache will be filled with dirty items
        /// Also a crash will mean a big redownload of the chain.
        /// </remarks>
        /// <seealso cref="lastCacheFlushTime"/>
        public int CacheFlushTimeIntervalSeconds { get; set; }

        /// <summary>Instance logger.</summary>
        private readonly ILogger logger;

        /// <summary>Maximum number of transactions in the cache.</summary>
        public int MaxCacheSizeBytes { get; set; }

        /// <summary>Statistics of hits and misses in the cache.</summary>
        private CachePerformanceCounter performanceCounter { get; set; }

        /// <summary>Lock object to protect access to <see cref="cachedUtxoItems"/>, <see cref="blockHash"/>, <see cref="cachedRewindData"/>, and <see cref="innerBlockHash"/>.</summary>
        private readonly object lockobj;

        /// <summary>Hash of the block headers of the tip of the coinview.</summary>
        /// <remarks>All access to this object has to be protected by <see cref="lockobj"/>.</remarks>
        private HashHeightPair blockHash;

        /// <summary>Hash of the block headers of the tip of the underlaying coinview.</summary>
        /// <remarks>All access to this object has to be protected by <see cref="lockobj"/>.</remarks>
        private HashHeightPair innerBlockHash;

        /// <summary>Coin view at one layer below this implementaiton.</summary>
        private readonly ICoindb coindb;

        /// <summary>Pending list of rewind data to be persisted to a persistent storage.</summary>
        /// <remarks>All access to this list has to be protected by <see cref="lockobj"/>.</remarks>
        private readonly Dictionary<int, RewindData> cachedRewindData;

#pragma warning disable SA1648 // inheritdoc must be used with inheriting class
        /// <inheritdoc />
        public ICoindb ICoindb => this.coindb;
#pragma warning restore SA1648 // inheritdoc must be used with inheriting class

        /// <summary>Storage of POS block information.</summary>
        private readonly StakeChainStore stakeChainStore;

        /// <summary>
        /// The rewind data index store.
        /// </summary>
        private readonly IRewindDataIndexCache rewindDataIndexCache;

        /// <summary>Information about cached items mapped by transaction IDs the cached item's unspent outputs belong to.</summary>
        /// <remarks>All access to this object has to be protected by <see cref="lockobj"/>.</remarks>
        private readonly Dictionary<OutPoint, CacheItem> cachedUtxoItems;

        /// <summary>Tracks pending balance updates for dirty cache entries.</summary>
        /// <remarks>All access to this object has to be protected by <see cref="lockobj"/>.</remarks>
        private readonly Dictionary<TxDestination, Dictionary<uint, long>> cacheBalancesByDestination;

        /// <summary>Number of items in the cache.</summary>
        /// <remarks>The getter violates the lock contract on <see cref="cachedUtxoItems"/>, but the lock here is unnecessary as the <see cref="cachedUtxoItems"/> is marked as readonly.</remarks>
        private int cacheCount => this.cachedUtxoItems.Count;

        /// <summary>Number of items in the rewind data.</summary>
        /// <remarks>The getter violates the lock contract on <see cref="cachedRewindData"/>, but the lock here is unnecessary as the <see cref="cachedRewindData"/> is marked as readonly.</remarks>
        private int rewindDataCount => this.cachedRewindData.Count;

        private long dirtyCacheCount;
        private long cacheSizeBytes;
        private long rewindDataSizeBytes;
        private DateTime lastCacheFlushTime;
        private readonly Network network;
        private readonly IDateTimeProvider dateTimeProvider;
        private readonly CancellationTokenSource cancellationToken;
        private IConsensusManager consensusManager;
        private readonly ConsensusSettings consensusSettings;
        private readonly ChainIndexer chainIndexer;
        private readonly bool addressIndexingEnabled;
        private CachePerformanceSnapshot latestPerformanceSnapShot;
        private IScriptAddressReader scriptAddressReader;

        private readonly Random random;

        public CachedCoinView(Network network, ICoindb coindb, IDateTimeProvider dateTimeProvider, ILoggerFactory loggerFactory, INodeStats nodeStats, ConsensusSettings consensusSettings, ChainIndexer chainIndexer,
            StakeChainStore stakeChainStore = null, IRewindDataIndexCache rewindDataIndexCache = null, IScriptAddressReader scriptAddressReader = null, INodeLifetime nodeLifetime = null, NodeSettings nodeSettings = null)
        {
            Guard.NotNull(coindb, nameof(CachedCoinView.coindb));

            this.coindb = coindb;
            this.logger = loggerFactory.CreateLogger(this.GetType().FullName);
            this.network = network;
            this.dateTimeProvider = dateTimeProvider;
            this.consensusSettings = consensusSettings;
            this.chainIndexer = chainIndexer;
            this.stakeChainStore = stakeChainStore;
            this.rewindDataIndexCache = rewindDataIndexCache;
            this.cancellationToken = (nodeLifetime == null) ? new CancellationTokenSource() : CancellationTokenSource.CreateLinkedTokenSource(nodeLifetime.ApplicationStopping);
            this.lockobj = new object();
            this.cachedUtxoItems = new Dictionary<OutPoint, CacheItem>();
            this.cacheBalancesByDestination = new Dictionary<TxDestination, Dictionary<uint, long>>();
            this.performanceCounter = new CachePerformanceCounter(this.dateTimeProvider);
            this.lastCacheFlushTime = this.dateTimeProvider.GetUtcNow();
            this.cachedRewindData = new Dictionary<int, RewindData>();
            this.scriptAddressReader = scriptAddressReader;
            this.addressIndexingEnabled = nodeSettings?.ConfigReader.GetOrDefault("addressindex", false) ?? false;
            this.random = new Random();

            this.MaxCacheSizeBytes = consensusSettings.MaxCoindbCacheInMB * 1024 * 1024;
            this.CacheFlushTimeIntervalSeconds = consensusSettings.CoindbIbdFlushMin * 60;

            if (nodeStats.DisplayBenchStats)
                nodeStats.RegisterStats(this.AddBenchStats, StatsType.Benchmark, this.GetType().Name, 300);
        }

        /// <summary>
        /// Remain on-chain.
        /// </summary>
        public void Sync()
        {
            lock (this.lockobj)
            {
                HashHeightPair coinViewTip = this.GetTipHash();
                if (coinViewTip.Hash == this.chainIndexer.Tip.HashBlock)
                    return;

                Flush();

                if (coinViewTip.Height > this.chainIndexer.Height || this.chainIndexer[coinViewTip.Hash] == null)
                {
                    // The coinview tip is above the chain height or on a fork.
                    // Determine the first unusable height by finding the first rewind data that is not on the consensus chain.
                    int unusableHeight = BinarySearch.BinaryFindFirst(h => (h > this.chainIndexer.Height) || (this.GetRewindData(h)?.PreviousBlockHash.Hash != this.chainIndexer[h].Previous.HashBlock), 2, coinViewTip.Height - 1);
                    ChainedHeader fork = this.chainIndexer[unusableHeight - 2];

                    while (coinViewTip.Height != fork.Height)
                    {
                        if ((coinViewTip.Height % 100) == 0)
                            this.logger.LogInformation("Rewinding coin view from '{0}' to {1}.", coinViewTip, fork);

                        // If the block store was initialized behind the coin view's tip, rewind it to on or before it's tip.
                        // The node will complete loading before connecting to peers so the chain will never know that a reorg happened.
                        coinViewTip = this.coindb.Rewind(new HashHeightPair(fork));
                    };

                    this.blockHash = coinViewTip;
                    this.innerBlockHash = this.blockHash;
                }

                CatchUp();
            }
        }

        private void CatchUp()
        {
            ChainedHeader chainTip = this.chainIndexer.Tip;
            HashHeightPair coinViewTip = this.coindb.GetTipHash();

            // If the coin view is behind the block store then catch up from the block store.
            if (coinViewTip.Height < chainTip.Height)
            {
                try
                {
                    IConsensusRuleEngine consensusRuleEngine = this.consensusManager.ConsensusRules;

                    var loadCoinViewRule = consensusRuleEngine.GetRule<LoadCoinviewRule>();
                    var saveCoinViewRule = consensusRuleEngine.GetRule<SaveCoinviewRule>();
                    var coinViewRule = consensusRuleEngine.GetRule<CoinViewRule>();
                    var deploymentsRule = consensusRuleEngine.GetRule<SetActivationDeploymentsFullValidationRule>();

                    foreach (ChainedHeaderBlock chb in this.consensusManager.GetBlocksAfterBlock(this.chainIndexer[coinViewTip.Hash], 1000, this.cancellationToken))
                    {
                        if (chb == null)
                            break;

                        ChainedHeader chainedHeader = chb.ChainedHeader;
                        Block block = chb.Block;

                        if (block == null)
                            break;

                        if ((chainedHeader.Height % 10000) == 0)
                        {
                            this.Flush(true);
                            this.logger.LogInformation("Rebuilding coin view from '{0}' to {1}.", chainedHeader, chainTip);
                        }

                        var utxoRuleContext = consensusRuleEngine.CreateRuleContext(new ValidationContext() { ChainedHeaderToValidate = chainedHeader, BlockToValidate = block });
                        utxoRuleContext.SkipValidation = true;

                        // Set context flags.
                        deploymentsRule.RunAsync(utxoRuleContext).ConfigureAwait(false).GetAwaiter().GetResult();

                        // Loads the coins spent by this block into utxoRuleContext.UnspentOutputSet.
                        loadCoinViewRule.RunAsync(utxoRuleContext).ConfigureAwait(false).GetAwaiter().GetResult();

                        // Spends the coins.
                        coinViewRule.RunAsync(utxoRuleContext).ConfigureAwait(false).GetAwaiter().GetResult();

                        // Saves the changes to the coinview.
                        saveCoinViewRule.RunAsync(utxoRuleContext).ConfigureAwait(false).GetAwaiter().GetResult(); 
                    }
                }
                finally
                {
                    this.Flush(true);

                    if (this.cancellationToken.IsCancellationRequested)
                    {
                        this.logger.LogDebug("Rebuilding cancelled due to application stopping.");
                        throw new OperationCanceledException();
                    }
                }
            }
        }

        public void Initialize(IConsensusManager consensusManager)
        {
            this.consensusManager = consensusManager;

            this.coindb.Initialize(this.addressIndexingEnabled);

            Sync();

            this.logger.LogInformation("Coin view initialized at '{0}'.", this.coindb.GetTipHash());
        }

        public HashHeightPair GetTipHash()
        {
            lock (this.lockobj)
            {
                if (this.blockHash == null)
                {
                    HashHeightPair response = this.coindb.GetTipHash();

                    this.innerBlockHash = response;
                    this.blockHash = this.innerBlockHash;
                }

                return this.blockHash;
            }
        }

        /// <inheritdoc />
        public void CacheCoins(OutPoint[] utxos)
        {
            lock (this.lockobj)
            {
                var missedOutpoint = new List<OutPoint>();
                foreach (OutPoint outPoint in utxos)
                {
                    if (!this.cachedUtxoItems.TryGetValue(outPoint, out CacheItem cache))
                    {
                        this.logger.LogDebug("Prefetch Utxo '{0}' not found in cache.", outPoint);
                        missedOutpoint.Add(outPoint);
                    }
                }

                this.performanceCounter.AddCacheMissCount(missedOutpoint.Count);
                this.performanceCounter.AddCacheHitCount(utxos.Length - missedOutpoint.Count);

                if (missedOutpoint.Count > 0)
                {
                    FetchCoinsResponse fetchedCoins = this.coindb.FetchCoins(missedOutpoint.ToArray());
                    foreach (var unspentOutput in fetchedCoins.UnspentOutputs)
                    {
                        var cache = new CacheItem()
                        {
                            ExistInInner = unspentOutput.Value.Coins != null,
                            IsDirty = false,
                            OutPoint = unspentOutput.Key,
                            Coins = unspentOutput.Value.Coins
                        };
                        this.logger.LogTrace("Prefetch CacheItem added to the cache, UTXO: '{0}', Coin:'{1}'.", cache.OutPoint, cache.Coins);
                        this.cachedUtxoItems.Add(cache.OutPoint, cache);
                        this.cacheSizeBytes += cache.GetSize;
                    }
                }
            }
        }

        /// <inheritdoc />
        public FetchCoinsResponse FetchCoins(OutPoint[] utxos)
        {
            Guard.NotNull(utxos, nameof(utxos));

            var result = new FetchCoinsResponse();
            var missedOutpoint = new List<OutPoint>();

            lock (this.lockobj)
            {
                foreach (OutPoint outPoint in utxos)
                {
                    if (!this.cachedUtxoItems.TryGetValue(outPoint, out CacheItem cache))
                    {
                        this.logger.LogTrace("Utxo '{0}' not found in cache.", outPoint);
                        missedOutpoint.Add(outPoint);
                    }
                    else
                    {
                        this.logger.LogTrace("Utxo '{0}' found in cache, UTXOs:'{1}'.", outPoint, cache.Coins);
                        result.UnspentOutputs.Add(outPoint, new UnspentOutput(outPoint, cache.Coins));
                    }
                }

                this.performanceCounter.AddMissCount(missedOutpoint.Count);
                this.performanceCounter.AddHitCount(utxos.Length - missedOutpoint.Count);

                if (missedOutpoint.Count > 0)
                {
                    this.logger.LogTrace("{0} cache missed transaction needs to be loaded from underlying CoinView.", missedOutpoint.Count);
                    FetchCoinsResponse fetchedCoins = this.coindb.FetchCoins(missedOutpoint.ToArray());

                    foreach (var unspentOutput in fetchedCoins.UnspentOutputs)
                    {
                        result.UnspentOutputs.Add(unspentOutput.Key, unspentOutput.Value);

                        var cache = new CacheItem()
                        {
                            ExistInInner = unspentOutput.Value.Coins != null,
                            IsDirty = false,
                            OutPoint = unspentOutput.Key,
                            Coins = unspentOutput.Value.Coins
                        };

                        this.logger.LogTrace("CacheItem added to the cache, UTXO '{0}', Coin:'{1}'.", cache.OutPoint, cache.Coins);
                        this.cachedUtxoItems.Add(cache.OutPoint, cache);
                        this.cacheSizeBytes += cache.GetSize;
                    }
                }

                // Check if we need to evict items form the cache.
                // This happens every time data is fetched fomr coindb

                this.TryEvictCacheLocked();
            }

            return result;
        }

        /// <summary>
        /// Deletes some items from the cache to free space for new items.
        /// Only items that are persisted in the underlaying storage can be deleted from the cache.
        /// </summary>
        /// <remarks>Should be protected by <see cref="lockobj"/>.</remarks>
        private void TryEvictCacheLocked()
        {
            // Calculate total size of cache.
            long totalBytes = this.cacheSizeBytes + this.rewindDataSizeBytes;

            if (totalBytes > this.MaxCacheSizeBytes)
            {
                this.logger.LogDebug("Cache is full now with {0} bytes, evicting.", totalBytes);

                List<CacheItem> itemsToRemove = new List<CacheItem>();
                foreach (KeyValuePair<OutPoint, CacheItem> entry in this.cachedUtxoItems)
                {
                    if (!entry.Value.IsDirty && entry.Value.ExistInInner)
                    {
                        if ((this.random.Next() % 3) == 0)
                        {
                            itemsToRemove.Add(entry.Value);
                        }
                    }
                }

                foreach (CacheItem item in itemsToRemove)
                {
                    this.logger.LogDebug("Transaction Id '{0}' selected to be removed from the cache, CacheItem:'{1}'.", item.OutPoint, item.Coins);
                    this.cachedUtxoItems.Remove(item.OutPoint);
                    this.cacheSizeBytes -= item.GetSize;
                    if (item.IsDirty) this.dirtyCacheCount--;
                }
            }
        }

        /// <summary>
        /// Finds all changed records in the cache and persists them to the underlying coinview.
        /// </summary>
        /// <param name="force"><c>true</c> to enforce flush, <c>false</c> to flush only if <see cref="lastCacheFlushTime"/> is older than <see cref="CacheFlushTimeIntervalSeconds"/>.</param>
        /// <remarks>
        /// WARNING: This method can only be run from <see cref="ConsensusLoop.Execute(System.Threading.CancellationToken)"/> thread context
        /// or when consensus loop is stopped. Otherwise, there is a risk of race condition when the consensus loop accepts new block.
        /// </remarks>
        public void Flush(bool force = true)
        {
            lock (this.lockobj)
            {
                if (!force)
                {
                    // Check if periodic flush is required.
                    // Ideally this will flush less frequent and always be behind 
                    // blockstore which is currently set to 17 sec.

                    DateTime now = this.dateTimeProvider.GetUtcNow();
                    bool flushTimeLimit = (now - this.lastCacheFlushTime).TotalSeconds >= this.CacheFlushTimeIntervalSeconds;

                    // The size of the cache was reached and most likely TryEvictCacheLocked didn't work
                    // so the cahces is pulledted with flushable items, then we flush anyway.

                    long totalBytes = this.cacheSizeBytes + this.rewindDataSizeBytes;
                    bool flushSizeLimit = totalBytes > this.MaxCacheSizeBytes;

                    if (!flushTimeLimit && !flushSizeLimit)
                    {
                        return;
                    }

                    this.logger.LogDebug("Flushing, reasons flushTimeLimit={0} flushSizeLimit={1}.", flushTimeLimit, flushSizeLimit);
                }

                // Before flushing the coinview persist the stake store
                // the stake store depends on the last block hash
                // to be stored after the stake store is persisted.
                if (this.stakeChainStore != null)
                    this.stakeChainStore.Flush(true);

                // Before flushing the coinview persist the rewind data index store as well.
                if (this.rewindDataIndexCache != null)
                    this.rewindDataIndexCache.SaveAndEvict(this.blockHash.Height, null);

                if (this.innerBlockHash == null)
                    this.innerBlockHash = this.coindb.GetTipHash();

                if (this.innerBlockHash == null)
                {
                    this.logger.LogTrace("(-)[NULL_INNER_TIP]");
                    return;
                }

                var modify = new List<UnspentOutput>();
                foreach (var cacheItem in this.cachedUtxoItems.Where(u => u.Value.IsDirty))
                {
                    cacheItem.Value.IsDirty = false;
                    cacheItem.Value.ExistInInner = true;

                    modify.Add(new UnspentOutput(cacheItem.Key, cacheItem.Value.Coins));
                }

                this.logger.LogDebug("Flushing {0} items.", modify.Count);

                this.coindb.SaveChanges(modify, this.cacheBalancesByDestination, this.innerBlockHash, this.blockHash, this.cachedRewindData.Select(c => c.Value).ToList());

                // All the cached utxos are now on disk so we can clear the cached entry list.
                this.cachedUtxoItems.Clear();
                this.cacheBalancesByDestination.Clear();
                this.cacheSizeBytes = 0;

                this.cachedRewindData.Clear();
                this.rewindDataSizeBytes = 0;
                this.dirtyCacheCount = 0;
                this.innerBlockHash = this.blockHash;                
                this.lastCacheFlushTime = this.dateTimeProvider.GetUtcNow();
            }
        }

        /// <inheritdoc />
        public void SaveChanges(IList<UnspentOutput> outputs, HashHeightPair oldBlockHash, HashHeightPair nextBlockHash, List<RewindData> rewindDataList = null)
        {
            Guard.NotNull(oldBlockHash, nameof(oldBlockHash));
            Guard.NotNull(nextBlockHash, nameof(nextBlockHash));
            Guard.NotNull(outputs, nameof(outputs));

            lock (this.lockobj)
            {
                if ((this.blockHash != null) && (oldBlockHash != this.blockHash))
                {
                    this.logger.LogDebug("{0}:'{1}'", nameof(this.blockHash), this.blockHash);
                    this.logger.LogTrace("(-)[BLOCKHASH_MISMATCH]");
                    throw new InvalidOperationException("Invalid oldBlockHash");
                }

                this.blockHash = nextBlockHash;
                long utxoSkipDisk = 0;

                var rewindData = new RewindData(oldBlockHash);
                Dictionary<OutPoint, int> indexItems = null;
                if (this.rewindDataIndexCache != null)
                    indexItems = new Dictionary<OutPoint, int>();

                foreach (UnspentOutput output in outputs)
                {
                    if (!this.cachedUtxoItems.TryGetValue(output.OutPoint, out CacheItem cacheItem))
                    {
                        // Add outputs to cache, this will happen for two cases
                        // 1. if a chaced item was evicted
                        // 2. for new outputs that are added

                        if (output.CreatedFromBlock)
                        {
                            // if the output is indicate that it was added from a block
                            // There is no need to spend an extra call to disk.

                            this.logger.LogDebug("New Outpoint '{0}' created.", output.OutPoint);

                            cacheItem = new CacheItem()
                            {
                                ExistInInner = false,
                                IsDirty = false,
                                OutPoint = output.OutPoint,
                                Coins = null
                            };
                        }
                        else
                        {
                            // This can happen if the cashe item was evicted while
                            // the block was being processed, fetch the outut again from disk.

                            this.logger.LogDebug("Outpoint '{0}' is not found in cache, creating it.", output.OutPoint);

                            FetchCoinsResponse result = this.coindb.FetchCoins(new[] { output.OutPoint });
                            this.performanceCounter.AddMissCount(1);

                            UnspentOutput unspentOutput = result.UnspentOutputs.Single().Value;

                            cacheItem = new CacheItem()
                            {
                                ExistInInner = unspentOutput.Coins != null,
                                IsDirty = false,
                                OutPoint = unspentOutput.OutPoint,
                                Coins = unspentOutput.Coins
                            };
                        }

                        this.cachedUtxoItems.Add(cacheItem.OutPoint, cacheItem);
                        this.cacheSizeBytes += cacheItem.GetSize;
                        this.logger.LogDebug("CacheItem added to the cache during save '{0}'.", cacheItem.OutPoint);
                    }

                    // If output.Coins is null this means the utxo needs to be deleted
                    // otherwise this is a new utxo and we store it to cache.

                    if (output.Coins == null)
                    {
                        // DELETE COINS

                        // Record the UTXO as having been spent at this height.
                        if (cacheItem.Coins != null)
                            this.RecordBalanceChange(cacheItem.Coins.TxOut.ScriptPubKey, -cacheItem.Coins.TxOut.Value, (uint)nextBlockHash.Height);

                        // In cases of an output spent in the same block 
                        // it wont exist in cash or in disk so its safe to remove it
                        if (cacheItem.Coins == null)
                        {
                            if (cacheItem.ExistInInner)
                                throw new InvalidOperationException(string.Format("Missmtch between coins in cache and in disk for output {0}", cacheItem.OutPoint));
                        }
                        else
                        {
                            // Handle rewind data
                            this.logger.LogDebug("Create restore outpoint '{0}' in OutputsToRestore rewind data.", cacheItem.OutPoint);
                            rewindData.OutputsToRestore.Add(new RewindDataOutput(cacheItem.OutPoint, cacheItem.Coins));
                            rewindData.TotalSize += cacheItem.GetSize;

                            if (this.rewindDataIndexCache != null && indexItems != null)
                            {
                                indexItems[cacheItem.OutPoint] = this.blockHash.Height;
                            }
                        }

                        // If a spent utxo never made it to disk then no need to keep it in memory.
                        if (!cacheItem.ExistInInner)
                        {
                            this.logger.LogDebug("Utxo '{0}' is not in disk, removing from cache.", cacheItem.OutPoint);
                            this.cachedUtxoItems.Remove(cacheItem.OutPoint);
                            this.cacheSizeBytes -= cacheItem.GetSize;
                            utxoSkipDisk++;
                            if (cacheItem.IsDirty) this.dirtyCacheCount--;
                        }
                        else
                        {
                            // Now modify the cached items with the mutated data.
                            this.logger.LogDebug("Mark cache item '{0}' as spent .", cacheItem.OutPoint);

                            this.cacheSizeBytes -= cacheItem.GetScriptSize;
                            cacheItem.Coins = null;

                            // Delete output from cache but keep a the cache
                            // item reference so it will get deleted form disk

                            cacheItem.IsDirty = true;
                            this.dirtyCacheCount++;
                        }
                    }
                    else
                    {
                        // ADD COINS

                        // Update the balance.
                        this.RecordBalanceChange(output.Coins.TxOut.ScriptPubKey, output.Coins.TxOut.Value, output.Coins.Height);

                        if (cacheItem.Coins != null)
                        {
                            // Allow overrides.
                            // See https://github.com/bitcoin/bitcoin/blob/master/src/coins.cpp#L94

                            bool allowOverride = cacheItem.Coins.IsCoinbase && output.Coins != null;

                            if (!allowOverride)
                            {
                                throw new InvalidOperationException(string.Format("New coins override coins in cache or store, for output '{0}'", cacheItem.OutPoint));
                            }

                            this.logger.LogDebug("Coin override alllowed for utxo '{0}'.", cacheItem.OutPoint);

                            // Deduct the crurrent script size form the 
                            // total cache size, it will be added again later. 
                            this.cacheSizeBytes -= cacheItem.GetScriptSize;

                            // Clear this in order to calculate the cache sie
                            // this will get set later when overriden
                            cacheItem.Coins = null;
                        }

                        // Handle rewind data
                        // New trx so it needs to be deleted if a rewind happens.
                        this.logger.LogDebug("Adding output '{0}' to TransactionsToRemove rewind data.", cacheItem.OutPoint);
                        rewindData.OutputsToRemove.Add(cacheItem.OutPoint);
                        rewindData.TotalSize += cacheItem.GetSize;

                        // Put in the cache the new UTXOs.
                        this.logger.LogDebug("Mark cache item '{0}' as new .", cacheItem.OutPoint);

                        cacheItem.Coins = output.Coins;
                        this.cacheSizeBytes += cacheItem.GetScriptSize;

                        // Mark the cache item as dirty so it get persisted 
                        // to disk and not evicted form cache

                        cacheItem.IsDirty = true;
                        this.dirtyCacheCount++;
                    }
                }

                this.performanceCounter.AddUtxoSkipDiskCount(utxoSkipDisk);

                if (this.rewindDataIndexCache != null && indexItems.Any())
                {
                    this.rewindDataIndexCache.SaveAndEvict(this.blockHash.Height, indexItems);
                }

                // Add the most recent rewind data to the cache.
                this.cachedRewindData.Add(this.blockHash.Height, rewindData);
                this.rewindDataSizeBytes += rewindData.TotalSize;
            }
        }

        public HashHeightPair Rewind(HashHeightPair target = null)
        {
            lock (this.lockobj)
            {
                if (this.innerBlockHash == null)
                {
                    this.innerBlockHash = this.coindb.GetTipHash();
                }

                // Flush the entire cache before rewinding
                this.Flush(true);

                HashHeightPair hash = this.coindb.Rewind(target);

                foreach (KeyValuePair<OutPoint, CacheItem> cachedUtxoItem in this.cachedUtxoItems)
                {
                    // This is a protection check to ensure we are
                    // not deleting dirty items form the cache.

                    if (cachedUtxoItem.Value.IsDirty)
                        throw new InvalidOperationException("Items in cache are modified");
                }

                // All the cached utxos are now on disk so we can clear the cached entry list.
                this.cachedUtxoItems.Clear();
                this.cacheBalancesByDestination.Clear();
                this.cacheSizeBytes = 0;
                this.dirtyCacheCount = 0;

                this.innerBlockHash = hash;
                this.blockHash = hash;

                if (this.rewindDataIndexCache != null)
                    this.rewindDataIndexCache.Initialize(this.blockHash.Height, this);

                return hash;
            }
        }

        /// <inheritdoc />
        public RewindData GetRewindData(int height)
        {
            lock (this.lockobj)
            {
                if (this.cachedRewindData.TryGetValue(height, out RewindData existingRewindData))
                    return existingRewindData;

                return this.coindb.GetRewindData(height);
            }
        }

        [NoTrace]
        private void AddBenchStats(StringBuilder log)
        {
            log.AppendLine("======CachedCoinView Bench======");
            DateTime now = this.dateTimeProvider.GetUtcNow();
            var lastFlush = (now - this.lastCacheFlushTime).TotalMinutes;
            log.AppendLine("Last flush ".PadRight(20) + Math.Round(lastFlush, 2) + " min ago (flush every " + TimeSpan.FromSeconds(this.CacheFlushTimeIntervalSeconds).TotalMinutes + " min)");

            log.AppendLine("Coin cache tip ".PadRight(20) + this.blockHash.Height);
            log.AppendLine("Coin store tip ".PadRight(20) + this.innerBlockHash.Height);
            log.AppendLine("block store tip ".PadRight(20) + "tbd");
            log.AppendLine();

            log.AppendLine("Cache entries ".PadRight(20) + this.cacheCount + " items");
            log.AppendLine("Dirty cache entries ".PadRight(20) + this.dirtyCacheCount + " items");

            log.AppendLine("Rewind data entries ".PadRight(20) + this.rewindDataCount + " items");
            var cache = this.cacheSizeBytes;
            var rewind = this.rewindDataSizeBytes;
            double filledPercentage = Math.Round(((cache + rewind) / (double)this.MaxCacheSizeBytes) * 100, 2);
            log.AppendLine("Cache size".PadRight(20) + cache.BytesToMegaBytes() + " MB");
            log.AppendLine("Rewind data size".PadRight(20) + rewind.BytesToMegaBytes() + " MB");
            log.AppendLine("Total cache size".PadRight(20) + (cache + rewind).BytesToMegaBytes() + " MB / " + this.consensusSettings.MaxCoindbCacheInMB + " MB (" + filledPercentage + "%)");


            CachePerformanceSnapshot snapShot = this.performanceCounter.Snapshot();

            if (this.latestPerformanceSnapShot == null)
                log.AppendLine(snapShot.ToString());
            else
                log.AppendLine((snapShot - this.latestPerformanceSnapShot).ToString());

            this.latestPerformanceSnapShot = snapShot;
        }

        private void RecordBalanceChange(Script scriptPubKey, long satoshis, uint height)
        {
            if (!this.coindb.BalanceIndexingEnabled || scriptPubKey.Length == 0 || satoshis == 0)
                return;

            foreach (TxDestination txDestination in this.scriptAddressReader.GetDestinationFromScriptPubKey(this.network, scriptPubKey))
            {
                if (!this.cacheBalancesByDestination.TryGetValue(txDestination, out Dictionary<uint, long> value))
                {
                    value = new Dictionary<uint, long>();
                    this.cacheBalancesByDestination[txDestination] = value;
                }

                if (!value.TryGetValue(height, out long balance))
                    balance = 0;

                balance += satoshis;

                value[height] = balance;
            }
        }

        public IEnumerable<(uint, long)> GetBalance(TxDestination txDestination)
        {
            IEnumerable<(uint, long)> CachedBalances()
            {
                if (this.cacheBalancesByDestination.TryGetValue(txDestination, out Dictionary<uint, long> itemsByHeight))
                {
                    long balance = 0;

                    foreach (uint height in itemsByHeight.Keys.OrderBy(k => k))
                    {
                        balance += itemsByHeight[height];
                        yield return (height, balance);
                    }
                }
            }

            bool first = true;
            foreach ((uint height, long satoshis) in this.coindb.GetBalance(txDestination))
            {
                if (first)
                {
                    first = false;

                    foreach ((uint height2, long satoshis2) in CachedBalances().Reverse())
                        yield return (height2, satoshis2 + satoshis);
                }

                yield return (height, satoshis);
            }

            if (first)
                foreach ((uint height2, long satoshis2) in CachedBalances().Reverse())
                    yield return (height2, satoshis2);
        }
    }
}
