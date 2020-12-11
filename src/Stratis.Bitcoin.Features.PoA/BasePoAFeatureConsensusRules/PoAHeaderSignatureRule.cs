using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
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
    public class PoAHeaderSignatureRule : FullValidationConsensusRule
    {
        private PoABlockHeaderValidator validator;

        private ISlotsManager slotsManager;

        private uint maxReorg;

        private bool votingEnabled;

        private VotingManager votingManager;

        private IChainState chainState;

        private Network network;

        /// <inheritdoc />
        public override void Initialize()
        {
            base.Initialize();

            var engine = this.Parent as PoAConsensusRuleEngine;

            this.slotsManager = engine.SlotsManager;
            this.validator = engine.PoaHeaderValidator;
            this.votingManager = engine.VotingManager;
            this.chainState = engine.ChainState;
            this.network = this.Parent.Network;

            this.maxReorg = this.network.Consensus.MaxReorgLength;
            this.votingEnabled = ((PoAConsensusOptions)this.network.Consensus.Options).VotingEnabled;
        }

        public override async Task RunAsync(RuleContext context)
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

                try
                {
                    // Gather all past and present mining public keys.
                    IEnumerable<PubKey> genesisFederation = ((PoAConsensusOptions)this.network.Consensus.Options).GenesisFederationMembers.Select(m => m.PubKey);
                    var knownKeys = new HashSet<PubKey>(genesisFederation);
                    foreach (Poll poll in this.votingManager.GetApprovedPolls().Where(x => ((x.VotingData.Key == VoteKey.AddFederationMember) || (x.VotingData.Key == VoteKey.KickFederationMember))))
                    {
                        IFederationMember federationMember = ((PoAConsensusFactory)(this.network.Consensus.ConsensusFactory)).DeserializeFederationMember(poll.VotingData.Data);
                        knownKeys.Add(federationMember.PubKey);
                    }

                    // Try to provide the public key that signed the block.
                    var signature = ECDSASignature.FromDER(header.BlockSignature.Signature);
                    for (int recId = 0; recId < 4; recId++)
                    {
                        PubKey pubKeyForSig = PubKey.RecoverFromSignature(recId, signature, header.GetHash(), true);
                        if (pubKeyForSig == null)
                        {
                            this.Logger.LogDebug($"Could not match candidate keys to any known key.");
                            break;
                        }

                        this.Logger.LogDebug($"Attempting to match candidate key '{ pubKeyForSig.ToHex() }' to known keys.");

                        if (!knownKeys.Any(pk => pk == pubKeyForSig))
                            continue;

                        IEnumerable<PubKey> modifiedFederation = this.votingManager?.GetModifiedFederation(context.ValidationContext.ChainedHeaderToValidate).Select(m => m.PubKey) ?? genesisFederation;

                        this.Logger.LogDebug($"Block {context.ValidationContext.ChainedHeaderToValidate}:{context.ValidationContext.ChainedHeaderToValidate.Header.Time} is signed by '{pubKeyForSig.ToHex()}' but expected '{pubKey}' from: { string.Join(" ", modifiedFederation.Select(pk => pk.ToHex()))}.");

                        break;
                    };
                }
                catch (Exception) { }

                this.Logger.LogDebug("(-)[INVALID_SIGNATURE]");
                PoAConsensusErrors.InvalidHeaderSignature.Throw();
            }
        }
    }
}
