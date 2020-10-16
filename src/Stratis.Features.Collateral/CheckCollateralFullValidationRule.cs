using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using NBitcoin;
using Stratis.Bitcoin;
using Stratis.Bitcoin.Consensus;
using Stratis.Bitcoin.Consensus.Rules;
using Stratis.Bitcoin.Features.BlockStore.AddressIndexing;
using Stratis.Bitcoin.Features.PoA;
using Stratis.Bitcoin.Interfaces;
using Stratis.Bitcoin.Utilities;
using Stratis.Features.Collateral.CounterChain;

namespace Stratis.Features.Collateral
{
    /// <summary>Ensures that collateral requirement on counterpart chain is fulfilled for the federation member that produced a block.</summary>
    /// <remarks>Ignored in IBD.</remarks>
    public class CheckCollateralFullValidationRule : FullValidationConsensusRule
    {
        private readonly IInitialBlockDownloadState ibdState;

        private readonly ICollateralChecker collateralChecker;

        private readonly ISlotsManager slotsManager;

        private readonly IFullNode fullNode;

        private readonly IDateTimeProvider dateTime;

        private readonly Network network;

        /// <summary>For how many seconds the block should be banned in case collateral check failed.</summary>
        private readonly int collateralCheckBanDurationSeconds;

        public CheckCollateralFullValidationRule(IInitialBlockDownloadState ibdState, ICollateralChecker collateralChecker,
            ISlotsManager slotsManager, IFullNode fullNode, IDateTimeProvider dateTime, Network network)
        {
            this.network = network;
            this.ibdState = ibdState;
            this.collateralChecker = collateralChecker;
            this.slotsManager = slotsManager;
            this.fullNode = fullNode;
            this.dateTime = dateTime;

            this.collateralCheckBanDurationSeconds = (int)(this.network.Consensus.Options as PoAConsensusOptions).TargetSpacingSeconds / 2;
        }

        public override Task RunAsync(RuleContext context)
        {
            if (this.ibdState.IsInitialBlockDownload())
            {
                this.Logger.LogTrace("(-)[SKIPPED_IN_IBD]");
                return Task.CompletedTask;
            }

            var commitmentHeightEncoder = new CollateralHeightCommitmentEncoder(this.Logger);
            (int? commitmentHeight, uint? commitmentNetworkMagic) = commitmentHeightEncoder.DecodeCommitmentHeight(context.ValidationContext.BlockToValidate.Transactions.First());
            if (commitmentHeight == null)
            {
                // We return here as it is CheckCollateralCommitmentHeightRule's responsibility to perform this check.
                this.Logger.LogTrace("(-)SKIPPED_AS_COLLATERAL_COMMITMENT_HEIGHT_MISSING]");
                return Task.CompletedTask;
            }

            this.Logger.LogDebug("Commitment is: {0}. Magic is: {1}", commitmentHeight, commitmentNetworkMagic);

            // TODO: The code contained in the following "if" can be removed after most nodes have switched their mainchain to Strax.

            // Strategy:
            // 1. I'm a Cirrus miner on STRAX. If the block's miner is also on STRAX then check the collateral. Pass or Fail as appropriate.
            // 2. The block miner is on STRAT. If most nodes were on STRAT(prev round) then they will check the rule. Pass the rule.
            // 3. The miner is on STRAT and most nodes were on STRAX(prev round).Fail the rule.

            // 1. If the block miner is on STRAX then skip this code and go check the collateral.
            Network counterChainNetwork = this.fullNode.NodeService<CounterChainNetworkWrapper>().CounterChainNetwork;
            if (this.network.Name.StartsWith("Cirrus") && commitmentNetworkMagic != counterChainNetwork.Magic)
            {
                // 2. The block miner is on STRAT.
                IConsensusManager consensusManager = this.fullNode.NodeService<IConsensusManager>();
                int memberCount = 0;
                int membersOnDifferentCounterChain = 0;
                uint targetSpacing = this.slotsManager.GetRoundLengthSeconds();
                ChainedHeader chainedHeader = context.ValidationContext.ChainedHeaderToValidate;

                // Check the block being validated and any prior blocks in the same round.
                // We already know at this point that block will not be null so this loop executes 
                // at least once and memberCount will be at least 1.
                for (Block block = context.ValidationContext.BlockToValidate; 
                    block != null && (block.Header.Time + targetSpacing) >= context.ValidationContext.BlockToValidate.Header.Time;
                    chainedHeader = chainedHeader.Previous, block = chainedHeader?.Block ?? consensusManager.GetBlockData(chainedHeader.HashBlock).Block)
                {
                    (int? commitmentHeight2, uint? magic2) = commitmentHeightEncoder.DecodeCommitmentHeight(block.Transactions.First());
                    if (commitmentHeight2 == null)
                        continue;

                    if (magic2 != counterChainNetwork.Magic)
                        membersOnDifferentCounterChain++;

                    memberCount++;                    
                };

                // If most nodes were on STRAT(prev round) then they will check the rule. Pass the rule.
                // This condition will execute if everyone is still on STRAT.
                if (membersOnDifferentCounterChain * 2 > memberCount)
                {
                    this.Logger.LogTrace("(-)SKIPPED_DURING_SWITCHOVER]");
                    return Task.CompletedTask;
                }

                // 3. The miner is on STRAT and most nodes were on STRAX(prev round). Fail the rule.
                this.Logger.LogTrace("(-)[DISALLOW_STRAT_MINER]");
                PoAConsensusErrors.InvalidCollateralAmount.Throw();
            }

            // TODO: Both this and CollateralPoAMiner are using this chain's MaxReorg instead of the Counter chain's MaxReorg. Beware: fixing requires fork.
            int counterChainHeight = this.collateralChecker.GetCounterChainConsensusHeight();
            int maxReorgLength = AddressIndexer.GetMaxReorgOrFallbackMaxReorg(this.network);

            // Check if commitment height is less than `mainchain consensus tip height - MaxReorg`.
            if (commitmentHeight > counterChainHeight - maxReorgLength)
            {
                // Temporary reject the block since it's possible that due to network connectivity problem counter chain is out of sync and
                // we are relying on chain state old data. It is possible that when we advance on counter chain commitment height will be
                // sufficiently old.
                context.ValidationContext.RejectUntil = this.dateTime.GetUtcNow() + TimeSpan.FromSeconds(this.collateralCheckBanDurationSeconds);

                this.Logger.LogDebug("commitmentHeight is {0}, counterChainHeight is {1}.", commitmentHeight, counterChainHeight);

                this.Logger.LogTrace("(-)[COMMITMENT_TOO_NEW]");
                PoAConsensusErrors.InvalidCollateralAmountCommitmentTooNew.Throw();
            }

            IFederationMember federationMember = this.slotsManager.GetFederationMemberForTimestamp(context.ValidationContext.BlockToValidate.Header.Time);
            if (!this.collateralChecker.CheckCollateral(federationMember, commitmentHeight.Value))
            {
                // By setting rejectUntil we avoid banning a peer that provided a block.
                context.ValidationContext.RejectUntil = this.dateTime.GetUtcNow() + TimeSpan.FromSeconds(this.collateralCheckBanDurationSeconds);

                this.Logger.LogTrace("(-)[BAD_COLLATERAL]");
                PoAConsensusErrors.InvalidCollateralAmount.Throw();
            }

            return Task.CompletedTask;
        }
    }
}
