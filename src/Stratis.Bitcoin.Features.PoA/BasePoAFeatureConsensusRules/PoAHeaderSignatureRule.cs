using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using NBitcoin;
using Stratis.Bitcoin.Consensus;
using Stratis.Bitcoin.Consensus.Rules;
using Stratis.Bitcoin.Utilities;

namespace Stratis.Bitcoin.Features.PoA.BasePoAFeatureConsensusRules
{
    /// <summary>
    /// Estimates which public key should be used for timestamp of a header being
    /// validated and uses this public key to verify header's signature.
    /// </summary>
    public class PoAHeaderSignatureRule : FullValidationConsensusRule
    {
        private PoABlockHeaderValidator validator;

        private ISlotsManager slotsManager;

        private IFederationHistory federationHistory;

        private HashHeightPair lastCheckPoint;

        /// <inheritdoc />
        public override void Initialize()
        {
            base.Initialize();

            var engine = this.Parent as PoAConsensusRuleEngine;

            // TODO: Consider adding these via a constructor on this rule.
            this.slotsManager = engine.SlotsManager;
            this.federationHistory = engine.FederationHistory;
            this.validator = engine.PoaHeaderValidator;

            KeyValuePair<int, CheckpointInfo> lastCheckPoint = engine.Network.Checkpoints.LastOrDefault();
            this.lastCheckPoint = (lastCheckPoint.Value != null) ? new HashHeightPair(lastCheckPoint.Value.Hash, lastCheckPoint.Key) : null;
        }

        public override Task RunAsync(RuleContext context)
        {
            // Only start validating at the last checkpoint block.
            if (context.ValidationContext.ChainedHeaderToValidate.Height < (this.lastCheckPoint?.Height ?? 0))
                return Task.CompletedTask;

            ChainedHeader chainedHeader = context.ValidationContext.ChainedHeaderToValidate;

            // If we're evaluating a batch of received headers it's possible that we're so far beyond the current tip
            // that we have not yet processed all the votes that may determine the federation make-up.
            if (!this.federationHistory.CanGetFederationForBlock(chainedHeader))
            {
                // Mark header as insufficient to avoid banning the peer that presented it.
                // When we advance consensus we will be able to validate it.
                context.ValidationContext.InsufficientHeaderInformation = true;

                this.Logger.LogWarning("The polls repository is too far behind to reliably determine the federation members.");
                this.Logger.LogDebug("(-)[INVALID_SIGNATURE]");
                PoAConsensusErrors.InvalidHeaderSignature.Throw();
            }

            var header = chainedHeader.Header as PoABlockHeader;

            IFederationMember federationMember = this.federationHistory.GetFederationMemberForBlock(chainedHeader);
            PubKey pubKey = federationMember?.PubKey;
            if (pubKey == null || !this.validator.VerifySignature(pubKey, header))
            {
                this.Logger.LogWarning("The block signature could not be matched with a current federation member.");
                this.Logger.LogDebug("(-)[INVALID_SIGNATURE]");
                PoAConsensusErrors.InvalidHeaderSignature.Throw();
            }

            uint expectedSlot = this.slotsManager.GetMiningTimestamp(chainedHeader.Previous, chainedHeader.Header.Time, pubKey);

            if (chainedHeader.Header.Time != expectedSlot)
            {
                this.Logger.LogDebug("Block {0} was mined in the wrong slot by miner '{1}'. The timestamp on the miner's block is {2} seconds earlier than expected.", chainedHeader.Height, pubKey.ToHex(), expectedSlot - chainedHeader.Header.Time);
                this.Logger.LogTrace("(-)[TIME_TOO_EARLY]");
                ConsensusErrors.BlockTimestampTooEarly.Throw();
            }

            return Task.CompletedTask;
        }
    }
}