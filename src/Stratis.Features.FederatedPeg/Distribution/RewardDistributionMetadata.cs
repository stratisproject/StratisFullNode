using NBitcoin;

namespace Stratis.Features.FederatedPeg.Distribution
{
    public class RewardDistributionMetadata
    {
        public uint256 TransactionId { get; set; }

        public ulong MainChainHeaderTimestamp { get; set; }
    }
}
