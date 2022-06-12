using System;
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
using Stratis.Bitcoin.Features.Consensus.Rules.CommonRules;
using Stratis.Bitcoin.Interfaces;
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
        private readonly IBlockStore blockStore;
        private readonly ConsensusRulesContainer consensusRulesContainer;

        public PowConsensusRuleEngine(Network network, ILoggerFactory loggerFactory, IDateTimeProvider dateTimeProvider, ChainIndexer chainIndexer,
            NodeDeployments nodeDeployments, ConsensusSettings consensusSettings, ICheckpoints checkpoints, ICoinView utxoSet, IChainState chainState,
            IInvalidBlockHashStore invalidBlockHashStore, INodeStats nodeStats, IAsyncProvider asyncProvider, ConsensusRulesContainer consensusRulesContainer, IBlockStore blockStore)
            : base(network, loggerFactory, dateTimeProvider, chainIndexer, nodeDeployments, consensusSettings, checkpoints, chainState, invalidBlockHashStore, nodeStats, consensusRulesContainer)
        {
            this.logger = loggerFactory.CreateLogger(this.GetType().FullName);

            this.consensusRulesContainer = consensusRulesContainer;

            this.UtxoSet = utxoSet;
            this.prefetcher = new CoinviewPrefetcher(this.UtxoSet, chainIndexer, loggerFactory, asyncProvider, checkpoints);
            this.blockStore = blockStore;
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
        public override Task<RewindState> RewindAsync(HashHeightPair target)
        {
            var state = new RewindState()
            {
                BlockHash = this.UtxoSet.Rewind(target)
            };

            return Task.FromResult(state);
        }

        /// <inheritdoc />
        public override void Initialize(ChainedHeader chainTip)
        {
            base.Initialize(chainTip);

            var coinDatabase = ((CachedCoinView)this.UtxoSet).ICoindb;
            coinDatabase.Initialize(chainTip);

            HashHeightPair coinViewTip = coinDatabase.GetTipHash();

            while (true)
            {
                ChainedHeader pendingTip = chainTip.FindAncestorOrSelf(coinViewTip.Hash);

                if (pendingTip != null)
                    break;

                if ((coinViewTip.Height % 100) == 0)
                    this.logger.LogInformation("Rewinding coin view from '{0}' to {1}.", coinViewTip, chainTip);

                // If the block store was initialized behind the coin view's tip, rewind it to on or before it's tip.
                // The node will complete loading before connecting to peers so the chain will never know that a reorg happened.
                coinViewTip = coinDatabase.Rewind(new HashHeightPair(chainTip));
            }

            // If the coin view is behind the block store then catch up from the block store.
            if (coinViewTip.Height < chainTip.Height)
            {
                foreach ((ChainedHeader chainedHeader, Block block) in this.blockStore.BatchBlocksFrom(this.ChainIndexer[0], this.ChainIndexer))
                {
                    if (block == null)
                        break;

                    if ((chainedHeader.Height % 10000) == 0)
                        this.logger.LogInformation("Rebuilding coin view from '{0}' to {1}.", chainedHeader, chainTip);

                    var ruleContext = new PosRuleContext()
                    {
                        ValidationContext = new ValidationContext() { ChainedHeaderToValidate = chainedHeader, BlockToValidate = block },
                        SkipValidation = true
                    };

                    foreach (var rule in this.consensusRulesContainer.FullValidationRules)
                    {
                        rule.RunAsync(ruleContext).ConfigureAwait(false).GetAwaiter().GetResult();
                    }
                }
            }

            this.logger.LogInformation("Coin view initialized at '{0}'.", coinDatabase.GetTipHash());
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

            var cache = this.UtxoSet as CachedCoinView;
            if (cache != null)
            {
                this.logger.LogInformation("Flushing Cache CoinView.");
                cache.Flush();
            }

            ((IDisposable)((CachedCoinView)this.UtxoSet).ICoindb).Dispose();
        }
    }
}
