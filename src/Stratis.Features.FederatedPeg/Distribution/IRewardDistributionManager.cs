using System.Collections.Generic;
using NBitcoin;
using Stratis.Features.FederatedPeg.Wallet;

namespace Stratis.Features.FederatedPeg.Distribution
{
    public interface IRewardDistributionManager
    {
        List<Recipient> DistributeToMultisigNodes(int blockHeight, Money totalReward);

        /// <summary>
        /// Finds the proportion of blocks mined by each miner.
        /// Creates a corresponding list of recipient scriptPubKeys and reward amounts.
        /// </summary>
        List<Recipient> Distribute(int blockHeight, Money totalReward);
    }
}
