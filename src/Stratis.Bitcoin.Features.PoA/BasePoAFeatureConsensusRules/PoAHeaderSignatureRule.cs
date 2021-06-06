using System;
using System.Collections.Generic;
using System.Linq;
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

        // Tracks mining activity and federation size for one round's worth of blocks.
        private List<(PubKey pubKey, int federationSize, DateTimeOffset blockTime)> activity;
        private ChainedHeader activityTip;

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
            this.activity = new List<(PubKey pubKey, int federationSize, DateTimeOffset blockTime)>();
            this.activityTip = null;

            this.maxReorg = this.network.Consensus.MaxReorgLength;
        }

        public override async Task RunAsync(RuleContext context)
        {
            ChainedHeader chainedHeader = context.ValidationContext.ChainedHeaderToValidate;

            var header = chainedHeader.Header as PoABlockHeader;

            List<IFederationMember> federation = this.federationHistory.GetFederationForBlock(chainedHeader);

            PubKey pubKey = this.federationHistory.GetFederationMemberForBlock(context.ValidationContext.ChainedHeaderToValidate/*, federation*/)?.PubKey;

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
            var roundTime = this.slotsManager.GetRoundLength(federation.Count);
            int blockCounter = 0;

            var headers = chainedHeader.Previous
                .EnumerateToGenesis()
                .TakeWhile(h => (header.BlockTime - h.Header.BlockTime) >= roundTime && !(this.activityTip?.HashBlock == h.HashBlock))
                .Reverse()
                .ToArray();


            if (headers.FirstOrDefault()?.Previous?.HashBlock != this.activityTip?.HashBlock)
                this.activity.Clear();

            (IFederationMember[] miners, (List<IFederationMember> members, HashSet<IFederationMember> _)[] federations) = this.federationHistory.GetFederationMembersForBlocks(headers);

            this.activity.AddRange(Enumerable.Range(0, miners.Length)
                .Select(i => (miners[i].PubKey, federations[i].members.Count, headers[i].Header.BlockTime)));

            this.activityTip = chainedHeader.Previous;

            for (int i = this.activity.Count - 1; i >= 0; i--)
            {
                blockCounter += 1;

                (PubKey miner, int federationSize, DateTimeOffset blockTime) = this.activity[i];

                if ((header.BlockTime - blockTime) >= roundTime)
                    break;

                // If the miner is found again within the same round then throw a consensus error.
                if (miner != pubKey)
                    continue;

                // Mining slots shift when the federation changes. 
                // Only raise an error if the federation did not change.
                if (federationSize != federation.Count)
                    break;

                if (this.activity[i - 1].federationSize != federation.Count)
                    break;

                this.Logger.LogDebug("Block was mined by the same miner '{0}' as {1} blocks ({2})s ago and there was no federation change.", pubKey.ToHex(), blockCounter, (header.BlockTime - blockTime).TotalSeconds);
                this.Logger.LogTrace("(-)[TIME_TOO_EARLY]");
                ConsensusErrors.BlockTimestampTooEarly.Throw();
            }
        }
    }
}
