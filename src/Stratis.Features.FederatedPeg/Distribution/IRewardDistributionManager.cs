using System.Collections.Generic;
using NBitcoin;
using Stratis.Features.FederatedPeg.Wallet;

namespace Stratis.Features.FederatedPeg.Distribution
{
    public interface IRewardDistributionManager
    {
        /// <summary>
        /// For wSTRAX and SRC20 to ERC20 transfers, the multisig needs to have a fee for submitting and confirming the transaction on the external chain, paid out to them.
        /// </summary>
        /// <param name="depositId">The id of the transaction/deposit on Cirrus.</param>
        /// <param name="totalReward">The total fee that will be distributed to the applicable multisig nodes.</param>
        List<Recipient> DistributeToMultisigNodes(uint256 depositId, Money totalReward);

        /// <summary>
        /// Finds the proportion of blocks mined by each miner.
        /// Creates a corresponding list of recipient scriptPubKeys and reward amounts.
        /// </summary>
        List<Recipient> Distribute(int blockHeight, Money totalReward);
    }
}
