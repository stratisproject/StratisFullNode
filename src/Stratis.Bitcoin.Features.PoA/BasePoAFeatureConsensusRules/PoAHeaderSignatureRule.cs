using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using NBitcoin;
using Stratis.Bitcoin.Base;
using Stratis.Bitcoin.Consensus;
using Stratis.Bitcoin.Consensus.Rules;

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

        private uint maxReorg;

        private IChainState chainState;

        private Network network;

        /// <inheritdoc />
        public override void Initialize()
        {
            base.Initialize();

            var engine = this.Parent as PoAConsensusRuleEngine;

            // TODO: Consider adding these via a constructor on this rule.
            this.slotsManager = engine.SlotsManager;
            this.federationHistory = engine.FederationHistory;
            this.validator = engine.PoaHeaderValidator;
            this.chainState = engine.ChainState;
            this.network = this.Parent.Network;

            this.maxReorg = this.network.Consensus.MaxReorgLength;
        }

        public override async Task RunAsync(RuleContext context)
        {
            ChainedHeader chainedHeader = context.ValidationContext.ChainedHeaderToValidate;

            var header = chainedHeader.Header as PoABlockHeader;

            List<IFederationMember> federation = this.federationHistory.GetFederationForBlock(chainedHeader);

            PubKey pubKey = this.federationHistory.GetFederationMemberForBlock(context.ValidationContext.ChainedHeaderToValidate, federation)?.PubKey;

            if (pubKey == null || !this.validator.VerifySignature(pubKey, header))
            {
                ChainedHeader currentHeader = context.ValidationContext.ChainedHeaderToValidate;

                // If we're evaluating a batch of received headers it's possible that we're so far beyond the current tip
                // that we have not yet processed all the votes that may determine the federation make-up.
                bool mightBeInsufficient = currentHeader.Height - this.chainState.ConsensusTip.Height > this.maxReorg;
                if (mightBeInsufficient)
                {
                    // Mark header as insufficient to avoid banning the peer that presented it.
                    // When we advance consensus we will be able to validate it.
                    context.ValidationContext.InsufficientHeaderInformation = true;
                }

                this.Logger.LogDebug("(-)[INVALID_SIGNATURE]");
                PoAConsensusErrors.InvalidHeaderSignature.Throw();
            }

            /* TODO: Uncomment this in the next release.
            uint roundTime = this.slotsManager.GetRoundLengthSeconds(federation.Count);

            // Look at the last round of blocks to find the previous time that the miner mined.
            ChainedHeader prevHeader = context.ValidationContext.ChainedHeaderToValidate.Previous;
            while (prevHeader.Previous != null && (header.Time - prevHeader.Header.Time) < roundTime)
            {
                // If the miner is found again within the same round then throw a consensus error.
                PubKey nextPubKey = this.federationHistory.GetFederationMemberForBlock(prevHeader)?.PubKey;
                if (nextPubKey == pubKey)
                {
                    this.Logger.LogTrace("(-)[TIME_TOO_EARLY]");
                    ConsensusErrors.BlockTimestampTooEarly.Throw();
                }
                prevHeader = prevHeader.Previous;
            }
            */
        }
    }
}
