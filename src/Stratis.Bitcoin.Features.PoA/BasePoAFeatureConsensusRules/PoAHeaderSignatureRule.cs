using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using NBitcoin;
using Stratis.Bitcoin.Consensus;
using Stratis.Bitcoin.Consensus.Rules;
using Stratis.Bitcoin.Interfaces;
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

        private IInitialBlockDownloadState initialBlockDownloadState;

        private HashHeightPair lastCheckPoint;

        public PoAHeaderSignatureRule(IInitialBlockDownloadState initialBlockDownloadState)
        {
            this.initialBlockDownloadState = initialBlockDownloadState;
        }

        /// <inheritdoc />
        public override void Initialize()
        {
            base.Initialize();

            var engine = this.Parent as PoAConsensusRuleEngine;

            // TODO: Consider adding these via a constructor on this rule.
            this.slotsManager = engine.SlotsManager;
            this.federationHistory = engine.FederationHistory;
            this.validator = engine.PoaHeaderValidator;

            var lastCheckPoint = engine.Network.Checkpoints.LastOrDefault();
            this.lastCheckPoint = (lastCheckPoint.Value != null) ? new HashHeightPair(lastCheckPoint.Value.Hash, lastCheckPoint.Key) : null;
        }

        public override async Task RunAsync(RuleContext context)
        {
            // Only start validating at the last checkpoint block.
            if (this.initialBlockDownloadState.IsInitialBlockDownload() && context.ValidationContext.ChainedHeaderToValidate.Height <= (this.lastCheckPoint?.Height ?? 0))
            {
                if (context.ValidationContext.ChainedHeaderToValidate.Height < this.lastCheckPoint.Height)
                    return;

                // Ensure we're getting the right block at the last checkpoint height.
                if (context.ValidationContext.ChainedHeaderToValidate.GetAncestor(this.lastCheckPoint.Height)?.HashBlock != this.lastCheckPoint.Hash)
                {
                    this.Logger.LogWarning("Can't  validate signature due to being on wrong chain.");
                    this.Logger.LogDebug("(-)[INVALID_SIGNATURE]");
                    PoAConsensusErrors.InvalidHeaderSignature.Throw();
                }
            }

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

            PubKey pubKey = this.federationHistory.GetFederationMemberForBlock(chainedHeader)?.PubKey;
            if (pubKey == null || !this.validator.VerifySignature(pubKey, header))
            {
                this.Logger.LogWarning("The block signature could not be matched with a current federation member.");
                this.Logger.LogDebug("(-)[INVALID_SIGNATURE]");
                PoAConsensusErrors.InvalidHeaderSignature.Throw();
            }

            // Look at the last round of blocks to find the previous time that the miner mined.
            var roundTime = this.slotsManager.GetRoundLength(this.federationHistory.GetFederationForBlock(chainedHeader).Count);
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
        }
    }
}