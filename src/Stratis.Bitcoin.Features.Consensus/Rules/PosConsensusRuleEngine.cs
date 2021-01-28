using Microsoft.Extensions.Logging;
using NBitcoin;
using Stratis.Bitcoin.AsyncWork;
using Stratis.Bitcoin.Base;
using Stratis.Bitcoin.Base.Deployments;
using Stratis.Bitcoin.Configuration.Settings;
using Stratis.Bitcoin.Consensus;
using Stratis.Bitcoin.Consensus.Rules;
using Stratis.Bitcoin.Features.Consensus.CoinViews;
using Stratis.Bitcoin.Features.Consensus.Interfaces;
using Stratis.Bitcoin.Features.Consensus.ProvenBlockHeaders;
using Stratis.Bitcoin.Utilities;
using TracerAttributes;

namespace Stratis.Bitcoin.Features.Consensus.Rules
{
    /// <summary>
    /// Extension of consensus rules that provide access to a PoS store.
    /// </summary>
    /// <remarks>
    /// A Proof-Of-Stake blockchain as implemented in this code base represents a hybrid POS/POW consensus model.
    /// </remarks>
    public class PosConsensusRuleEngine : PowConsensusRuleEngine
    {
        /// <summary>Database of stake related data for the current blockchain.</summary>
        public IStakeChain StakeChain { get; }

        /// <summary>Provides functionality for checking validity of PoS blocks.</summary>
        public IStakeValidator StakeValidator { get; }

        public IRewindDataIndexCache RewindDataIndexCache { get; }

        private readonly IFinalizedBlockInfoRepository finalizedBlockInfoRepository;

        public PosConsensusRuleEngine(Network network, ILoggerFactory loggerFactory, IDateTimeProvider dateTimeProvider, ChainIndexer chainIndexer, NodeDeployments nodeDeployments,
            ConsensusSettings consensusSettings, ICheckpoints checkpoints, ICoinView utxoSet, IStakeChain stakeChain, IStakeValidator stakeValidator, IChainState chainState,
            IInvalidBlockHashStore invalidBlockHashStore, INodeStats nodeStats, IRewindDataIndexCache rewindDataIndexCache, IAsyncProvider asyncProvider, ConsensusRulesContainer consensusRulesContainer,
            IFinalizedBlockInfoRepository finalizedBlockInfoRepository = null)
            : base(network, loggerFactory, dateTimeProvider, chainIndexer, nodeDeployments, consensusSettings, checkpoints, utxoSet, chainState, invalidBlockHashStore, nodeStats, asyncProvider, consensusRulesContainer)
        {
            this.StakeChain = stakeChain;
            this.StakeValidator = stakeValidator;
            this.RewindDataIndexCache = rewindDataIndexCache;
            this.finalizedBlockInfoRepository = finalizedBlockInfoRepository;
        }

        /// <inheritdoc />
        [NoTrace]
        public override RuleContext CreateRuleContext(ValidationContext validationContext)
        {
            return new PosRuleContext(validationContext, this.DateTimeProvider.GetTimeOffset());
        }

        /// <inheritdoc />
        public override void Initialize(ChainedHeader chainTip)
        {
            base.Initialize(chainTip);

            this.StakeChain.Load();

            // Is the chain tip ok?
            // TODO: This is temporary code to give an earlier indication of why this error is raised in the Initialize call below. 
            if (this.finalizedBlockInfoRepository != null && chainTip.Height < this.finalizedBlockInfoRepository.GetFinalizedBlockInfo().Height)
                throw new ConsensusException($"Violation of finality on height { chainTip.Height } for RewindDataIndex.");

            // A temporary hack until tip manage will be introduced.
            var coindb = ((CachedCoinView)this.UtxoSet).ICoindb;
            HashHeightPair hash = coindb.GetTipHash();
            ChainedHeader tip = chainTip.FindAncestorOrSelf(hash.Hash);

            // Determine if the hack is leading to issues.
            // TODO: This is temporary code to give an earlier indication of why this error is raised in the Initialize call below. 
            if (this.finalizedBlockInfoRepository != null && tip.Height < this.finalizedBlockInfoRepository.GetFinalizedBlockInfo().Height)
                throw new ConsensusException($"Violation of finality on height { tip.Height } for RewindDataIndex.");

            this.RewindDataIndexCache.Initialize(tip.Height, this.UtxoSet);
        }
    }
}
