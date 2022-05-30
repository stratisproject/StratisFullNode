using NBitcoin;
using Stratis.Bitcoin.Base.Deployments;
using Stratis.Bitcoin.Consensus.Rules;

namespace Stratis.Bitcoin.Features.Consensus.Rules.CommonRules
{
    /// <summary>A base skeleton class that is implemented by networks to define and verify the version of blocks.</summary>
    public abstract class HeaderVersionRule : HeaderValidationConsensusRule
    {
        private readonly NodeDeployments nodeDeployments;

        protected HeaderVersionRule(NodeDeployments nodeDeployments)
        {
            this.nodeDeployments = nodeDeployments;
        }

        /// <summary>
        /// Computes what the block version of a newly created block should be, given a previous header and the
        /// current set of BIP9 deployments defined in the consensus.
        /// </summary>
        /// <param name="prevChainedHeader">The header of the previous block in the chain.</param>
        /// <remarks>This method is currently used during block creation only. Different nodes may not implement
        /// BIP9, or may disagree about what the current valid set of deployments are. It is therefore not strictly
        /// possible to validate a block version number in anything more than general terms.</remarks>
        /// <returns>The block version.</returns>
        public int ComputeBlockVersion(ChainedHeader prevChainedHeader)
        {
            uint version = ThresholdConditionCache.VersionbitsTopBits;

            for (int deployment = 0; deployment < this.nodeDeployments.BIP9.ArraySize; deployment++)
            {
                ThresholdState state = this.nodeDeployments.BIP9.GetState(prevChainedHeader, deployment);
                if ((state == ThresholdState.LockedIn) || (state == ThresholdState.Started))
                    version |= this.nodeDeployments.BIP9.Mask(deployment);
            }

            return (int)version;
        }
    }
}