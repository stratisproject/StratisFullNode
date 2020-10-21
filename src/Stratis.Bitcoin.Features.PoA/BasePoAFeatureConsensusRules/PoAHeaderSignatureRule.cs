using System.Collections.Generic;
using System.Linq;
using Microsoft.Extensions.Logging;
using NBitcoin;
using Stratis.Bitcoin.Base;
using Stratis.Bitcoin.Consensus.Rules;
using Stratis.Bitcoin.Features.PoA.Voting;

namespace Stratis.Bitcoin.Features.PoA.BasePoAFeatureConsensusRules
{
    /// <summary>
    /// Estimates which public key should be used for timestamp of a header being
    /// validated and uses this public key to verify header's signature.
    /// </summary>
    public class PoAHeaderSignatureRule : HeaderValidationConsensusRule
    {
        private PoABlockHeaderValidator validator;

        private ISlotsManager slotsManager;

        private uint maxReorg;

        private bool votingEnabled;

        private VotingManager votingManager;

        private IFederationManager federationManager;

        private IChainState chainState;

        private PoAConsensusFactory consensusFactory;

        /// <inheritdoc />
        public override void Initialize()
        {
            base.Initialize();

            var engine = this.Parent as PoAConsensusRuleEngine;

            this.slotsManager = engine.SlotsManager;
            this.validator = engine.PoaHeaderValidator;
            this.votingManager = engine.VotingManager;
            this.federationManager = engine.FederationManager;
            this.chainState = engine.ChainState;
            this.consensusFactory = (PoAConsensusFactory)this.Parent.Network.Consensus.ConsensusFactory;

            this.maxReorg = this.Parent.Network.Consensus.MaxReorgLength;
            this.votingEnabled = ((PoAConsensusOptions) this.Parent.Network.Consensus.Options).VotingEnabled;
        }

        public override void Run(RuleContext context)
        {
            var header = context.ValidationContext.ChainedHeaderToValidate.Header as PoABlockHeader;
            
            PubKey pubKey = this.slotsManager.GetFederationMemberForBlock(context.ValidationContext.ChainedHeaderToValidate, this.votingManager).PubKey;

            if (!this.validator.VerifySignature(pubKey, header))
            {
                if (this.votingEnabled)
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
                }

                this.Logger.LogTrace("(-)[INVALID_SIGNATURE]");
                PoAConsensusErrors.InvalidHeaderSignature.Throw();
            }
        }
    }
}
