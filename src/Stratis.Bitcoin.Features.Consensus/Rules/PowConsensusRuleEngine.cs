using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using NBitcoin;
using Stratis.Bitcoin.AsyncWork;
using Stratis.Bitcoin.Base;
using Stratis.Bitcoin.Base.Deployments;
using Stratis.Bitcoin.Configuration.Settings;
using Stratis.Bitcoin.Consensus;
using Stratis.Bitcoin.Consensus.Rules;
using Stratis.Bitcoin.Features.Consensus.CoinViews;
using Stratis.Bitcoin.Utilities;
using TracerAttributes;

namespace Stratis.Bitcoin.Features.Consensus.Rules
{
    /// <summary>
    /// Extension of consensus rules that provide access to a store based on UTXO (Unspent transaction outputs).
    /// </summary>
    public class PowConsensusRuleEngine : ConsensusRuleEngine
    {
        /// <summary>Instance logger.</summary>
        private readonly ILogger logger;

        /// <summary>The consensus db, containing all unspent UTXO in the chain.</summary>
        public ICoinView UtxoSet { get; }

        private readonly CoinviewPrefetcher prefetcher;

        public PowConsensusRuleEngine(Network network, ILoggerFactory loggerFactory, IDateTimeProvider dateTimeProvider, ChainIndexer chainIndexer,
            NodeDeployments nodeDeployments, ConsensusSettings consensusSettings, ICheckpoints checkpoints, ICoinView utxoSet, IChainState chainState,
            IInvalidBlockHashStore invalidBlockHashStore, INodeStats nodeStats, IAsyncProvider asyncProvider, ConsensusRulesContainer consensusRulesContainer)
            : base(network, loggerFactory, dateTimeProvider, chainIndexer, nodeDeployments, consensusSettings, checkpoints, chainState, invalidBlockHashStore, nodeStats, consensusRulesContainer)
        {
            this.logger = loggerFactory.CreateLogger(this.GetType().FullName);

            this.UtxoSet = utxoSet;
            this.prefetcher = new CoinviewPrefetcher(this.UtxoSet, chainIndexer, loggerFactory, asyncProvider, checkpoints);
        }

        /// <inheritdoc />
        [NoTrace]
        public override RuleContext CreateRuleContext(ValidationContext validationContext)
        {
            return new PowRuleContext(validationContext, this.DateTimeProvider.GetTimeOffset());
        }

        /// <inheritdoc />
        public override HashHeightPair GetBlockHash()
        {
            return this.UtxoSet.GetTipHash();
        }

        /// <inheritdoc />
        public override Task<RewindState> RewindAsync()
        {
            var state = new RewindState()
            {
                BlockHash = this.UtxoSet.Rewind()
            };

            return Task.FromResult(state);
        }

        /// <inheritdoc />
        public override void Initialize(ChainedHeader chainTip)
        {
            base.Initialize(chainTip);

            // TODO: Move this initialization out of the rules engine.
            var coinViewDatabase = ((CachedCoinView)this.UtxoSet).ICoinDb;
            coinViewDatabase.Initialize();

            HashHeightPair coinViewDatabaseTip = coinViewDatabase.GetTipHash();

            // If the chain's height is more than the current coinview's tip,
            // we need to inspect it's integrity.
            if (chainTip.Height > coinViewDatabaseTip.Height)
                RestoreCoinDatabaseIntegrity(coinViewDatabase, coinViewDatabaseTip.Height);

            while (true)
            {
                ChainedHeader pendingTip = chainTip.FindAncestorOrSelf(coinViewDatabaseTip.Hash);

                if (pendingTip != null)
                    break;

                this.logger.LogInformation("Rewinding coin db from {0}", coinViewDatabaseTip);

                // In case block store initialized behind, rewind until or before the block store tip.
                // The node will complete loading before connecting to peers so the chain will never know if a reorg happened.
                coinViewDatabaseTip = coinViewDatabase.Rewind();
            }
        }

        /// <summary>
        /// If, on startup, the chain's tip is higher than the coinview's tip,
        /// it is possible that the node got shutdown unexpectedly.
        /// In this scenario we need to inspect the coin database's rewind data to ensure
        /// that there aren't any outpoints that has been deleted/restored that shouldn't have been.
        /// We need to walk up the height from the current chain tip until there is no more rewind data and rewind said
        /// rewind items.
        /// </summary>
        /// <param name="coinDb">Access to the coin database.</param>
        /// <param name="coinViewHeight">The height to start the inspection from.</param>
        private void RestoreCoinDatabaseIntegrity(ICoindb coinDb, int coinViewHeight)
        {
            this.logger.LogInformation("The chain's tip is higher than the coinview's tip... Inspecting the coin database's outpoint integrity.");

            var rewindDataItems = new List<(RewindData RewindDataItem, int Height)>();

            // Determine whether there are rewind items that are persisted above the chain tip's height.
            var inspectionHeight = coinViewHeight + 1;
            do
            {
                var rewindDataItem = coinDb.GetRewindData(inspectionHeight);
                if (rewindDataItem == null)
                    break;

                rewindDataItems.Add((rewindDataItem, inspectionHeight));

                inspectionHeight += 1;

            } while (true);

            if (rewindDataItems.Count > 0)
            {
                this.logger.LogInformation($"{rewindDataItems.Count} rewind data items found that will need to be rewound.");

                foreach (var (RewindDataItem, Height) in rewindDataItems.OrderByDescending(r => r.Height))
                {
                    coinDb.RewindDataItem(RewindDataItem, Height);
                }

                this.logger.LogInformation($"Coin database integrity restored.");
            }
            else
                this.logger.LogInformation($"Coin database integrity check reported no issues.");
        }

        public override async Task<ValidationContext> FullValidationAsync(ChainedHeader header, Block block)
        {
            ValidationContext result = await base.FullValidationAsync(header, block).ConfigureAwait(false);

            if ((result != null) && (result.Error == null))
            {
                // Notify prefetch manager about block that was validated so prefetch manager
                // can decide what coins we will most likely need for full validation in the near future.
                this.prefetcher.Prefetch(header);
            }

            return result;
        }

        public override void Dispose()
        {
            this.prefetcher.Dispose();

            if (this.UtxoSet is CachedCoinView cache)
            {
                this.logger.LogInformation("Flushing Cache CoinView.");
                cache.Flush();
            }

            ((IDisposable)((CachedCoinView)this.UtxoSet).ICoinDb).Dispose();
        }
    }
}
