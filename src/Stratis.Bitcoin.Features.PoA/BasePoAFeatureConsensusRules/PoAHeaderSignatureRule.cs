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

            // Look at the last round of blocks to find the previous time that the miner mined.
            uint roundTime = this.slotsManager.GetRoundLengthSeconds(federation.Count);
            
            for (ChainedHeader prevHeader = chainedHeader.Previous; prevHeader.Previous != null; prevHeader = prevHeader.Previous)
            {
                if ((header.Time - prevHeader.Header.Time) >= roundTime)
                    break;

                // If the miner is found again within the same round then throw a consensus error.
                if (this.federationHistory.GetFederationMemberForBlock(prevHeader)?.PubKey != pubKey)
                    continue;

                // Mining slots shift when the federation changes. 
                // Only raise an error if the federation did not change.
                uint newRoundTime = this.slotsManager.GetRoundLengthSeconds(this.federationHistory.GetFederationForBlock(prevHeader).Count);
                if (newRoundTime != roundTime)
                    break;

                // Exempt this one block that somehow got included in the chain where the miner managed to mine too early.
                if (prevHeader.HashBlock == uint256.Parse("7d67ea42010f03971edd6ba5e1b644d09c9fd0191ca8d312255c12d23f7cd147"))
                    break;

                this.Logger.LogTrace("(-)[TIME_TOO_EARLY]");
                ConsensusErrors.BlockTimestampTooEarly.Throw();
            }
        }
    }
}
