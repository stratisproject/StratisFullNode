using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using NBitcoin;
using Stratis.Bitcoin.Base.Deployments;
using Stratis.Bitcoin.Consensus.Rules;
using Stratis.Bitcoin.Features.PoA;
using Stratis.Features.PoA.Collateral;

namespace Stratis.Features.Collateral
{
    /// <summary>
    /// Ensures that a block that was produced on a collateral aware network contains height commitment data in the coinbase transaction.
    /// <para>
    /// Blocks that are found to have this data missing data will have the peer that served the header, banned.
    /// </para>
    /// </summary>
    public sealed class CheckCollateralCommitmentHeightRule : FullValidationConsensusRule
    {
        public override Task RunAsync(RuleContext context)
        {
            // The genesis block won't contain any commitment data.
            if (context.ValidationContext.ChainedHeaderToValidate.Height == 0)
                return Task.CompletedTask;

            // If the network's CollateralCommitmentActivationHeight is set then check that the current height is less than than that.
            if (context.ValidationContext.ChainedHeaderToValidate.Height < (this.Parent.Network as PoANetwork).CollateralCommitmentActivationHeight)
                return Task.CompletedTask;

            var commitmentHeightEncoder = new CollateralHeightCommitmentEncoder();

            (int? commitmentHeight, _) = commitmentHeightEncoder.DecodeCommitmentHeight(context.ValidationContext.BlockToValidate.Transactions.First());
            if (commitmentHeight == null)
            {
                // Every PoA miner on a sidechain network is forced to include commitment data to the blocks mined.
                // Not having a commitment should always result in a permanent ban of the block.
                this.Logger.LogTrace("(-)[COLLATERAL_COMMITMENT_HEIGHT_MISSING]");
                PoAConsensusErrors.CollateralCommitmentHeightMissing.Throw();
            }

            // Check that the commitment height is not less that of the prior block.
            int release1340ActivationHeight = 0;
            NodeDeployments nodeDeployments = this.Parent.NodeDeployments;
            if (nodeDeployments.BIP9.ArraySize > 0  /* Not NoBIP9Deployments */)
                release1340ActivationHeight = nodeDeployments.BIP9.ActivationHeightProviders[1 /* Release1340 */].ActivationHeight;

            if (context.ValidationContext.ChainedHeaderToValidate.Height >= release1340ActivationHeight)
            {
                ChainedHeader prevHeader = context.ValidationContext.ChainedHeaderToValidate.Previous;
                if (prevHeader.BlockDataAvailability == BlockDataAvailabilityState.BlockAvailable)
                {
                    if (prevHeader.Block != null)
                    {
                        (int? commitmentHeightPrev, _) = commitmentHeightEncoder.DecodeCommitmentHeight(prevHeader.Block.Transactions.First());
                        if (commitmentHeight < commitmentHeightPrev)
                        {
                            this.Logger.LogTrace("(-)[COLLATERAL_COMMITMENT_TOO_OLD]");
                            PoAConsensusErrors.InvalidCollateralAmountCommitmentTooOld.Throw();
                        }
                    }
                }
            }

            return Task.CompletedTask;
        }
    }
}
