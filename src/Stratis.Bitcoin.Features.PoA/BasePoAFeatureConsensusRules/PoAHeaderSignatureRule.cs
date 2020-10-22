using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Extensions.Logging;
using NBitcoin;
using NBitcoin.Crypto;
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

            List<IFederationMember> modifiedFederation = this.votingManager.GetModifiedFederation(context.ValidationContext.ChainedHeaderToValidate);
            PubKey pubKey = this.slotsManager.GetFederationMemberForTimestamp(context.ValidationContext.ChainedHeaderToValidate.Header.Time, modifiedFederation).PubKey;

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

                try 
                {
                    // Try to provide the public key that signed the block.
                    var signature = ECDSASignature.FromDER(header.BlockSignature.Signature);
                    for (int recId = 0; ; recId++)
                    {
                        PubKey pubKeyForSig = PubKey.RecoverFromSignature(recId, signature, header.GetHash(), true);
                        if (pubKeyForSig == null)
                            break;

                        this.Logger.LogDebug($"Matching candidate key '{ pubKeyForSig.ToHex() }' to federation at height { context.ValidationContext.ChainedHeaderToValidate.Height }.");

                        if (!modifiedFederation.Any(m => m.PubKey == pubKeyForSig))
                            continue;

                        this.Logger.LogDebug($"Block is signed by '{0}' but expected '{1}' from: {2}.", pubKeyForSig.ToHex(),
                            pubKey, string.Join(" ", modifiedFederation.Select(m => m.PubKey.ToHex())));

                        break;
                    };
                } 
                catch (Exception) { }

                this.Logger.LogTrace("(-)[INVALID_SIGNATURE]");
                PoAConsensusErrors.InvalidHeaderSignature.Throw();
            }
        }
    }
}
