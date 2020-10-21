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
                // In case voting is enabled it is possible that federation was modified and another fed member signed
                // the header. Since voting changes are applied after max reorg blocks are passed we can tell exactly
                // how federation will look like max reorg blocks ahead. Code below tries to construct federation that is
                // expected to exist at the moment block that corresponds to header being validated was produced. Then
                // this federation is used to estimate who was expected to sign a block and then the signature is verified.
                if (this.votingEnabled)
                {
                    ChainedHeader currentHeader = context.ValidationContext.ChainedHeaderToValidate;

                    bool mightBeInsufficient = currentHeader.Height - this.chainState.ConsensusTip.Height > this.maxReorg;

                    // Get the federation as it was at currentHeader.
                    pubKey = this.slotsManager.GetFederationMemberForBlock(context.ValidationContext.ChainedHeaderToValidate, this.votingManager).PubKey;

                    if (this.validator.VerifySignature(pubKey, header))
                    {
                        this.Logger.LogDebug("Signature verified using updated federation.");
                        return;
                    }

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
