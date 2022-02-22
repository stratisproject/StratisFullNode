﻿using System;
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

        private PoAConsensusOptions poAConsensusOptions;

        /// <inheritdoc />
        public override void Initialize()
        {
            base.Initialize();

            var engine = this.Parent as PoAConsensusRuleEngine;

            // TODO: Consider adding these via a constructor on this rule.
            this.slotsManager = engine.SlotsManager;
            this.federationHistory = engine.FederationHistory;
            this.validator = engine.PoaHeaderValidator;
            this.poAConsensusOptions = engine.Network.Consensus.Options as PoAConsensusOptions;

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

            if (chainedHeader.Height >= this.poAConsensusOptions.GetMiningTimestampV2ActivationStrictHeight)
            {
                uint expectedSlot = this.slotsManager.GetMiningTimestamp(chainedHeader.Previous, chainedHeader.Header.Time, pubKey);

                if (chainedHeader.Header.Time != expectedSlot)
                {
                    this.Logger.LogWarning("Block {0} was mined in the wrong slot by miner '{1}'. The timestamp on the miner's block is {2} seconds earlier than expected.", chainedHeader.Height, pubKey.ToHex(), expectedSlot - chainedHeader.Header.Time);
                    this.Logger.LogTrace("(-)[TIME_TOO_EARLY]");
                    ConsensusErrors.BlockTimestampTooEarly.Throw();
                }

                return Task.CompletedTask;
            }

            // TODO: Remove this code once the last checkpoint exceeds 'GetMiningTimestampV2ActivationStrictHeight'.
            // Look at the last round of blocks to find the previous time that the miner mined.
            TimeSpan roundTime = this.slotsManager.GetRoundLength(this.federationHistory.GetFederationForBlock(chainedHeader).Count);

            // Quick check for optimisation.
            this.federationHistory.GetLastActiveTime(federationMember, chainedHeader.Previous, out uint lastActiveTime);
            if ((chainedHeader.Header.Time - lastActiveTime) >= roundTime.TotalSeconds)
                return Task.CompletedTask;

            int blockCounter = 0;

            for (ChainedHeader prevHeader = chainedHeader.Previous; prevHeader.Previous != null; prevHeader = prevHeader.Previous)
            {
                blockCounter += 1;

                if ((header.BlockTime - prevHeader.Header.BlockTime) >= roundTime)
                    break;

                // If the miner is found again within the same round then throw a consensus error.
                if (this.federationHistory.GetFederationMemberForBlock(prevHeader)?.PubKey != pubKey)
                    continue;

                // Mining slots shift when the federation changes. 
                // Only raise an error if the federation did not change.
                if (this.slotsManager.GetRoundLength(this.federationHistory.GetFederationForBlock(prevHeader).Count) != roundTime)
                    break;

                if (this.slotsManager.GetRoundLength(this.federationHistory.GetFederationForBlock(prevHeader.Previous).Count) != roundTime)
                    break;

                this.Logger.LogDebug("Block {0} was mined by the same miner '{1}' as {2} blocks ({3})s ago and there was no federation change.", prevHeader.HashBlock, pubKey.ToHex(), blockCounter, header.Time - prevHeader.Header.Time);
                this.Logger.LogTrace("(-)[TIME_TOO_EARLY]");
                ConsensusErrors.BlockTimestampTooEarly.Throw();
            }

            return Task.CompletedTask;
        }
    }
}