using NBitcoin;

namespace Stratis.Features.FederatedPeg.Distribution
{
    public class DistributionRecord
    {
        /// <summary>
        /// The block the distribution initiation transaction appeared in (on the sidechain).
        /// </summary>
        public uint256 BlockHash { get; set; }

        /// <summary>
        /// The height of the block the distribution initiation transaction appeared in.
        /// </summary>
        public int BlockHeight { get; set; }

        /// <summary>
        /// The commitment height encoded in the block the distribution initiation transaction appeared in.
        /// </summary>
        public int CommitmentHeight { get; set; }

        /// <summary>
        /// The identifier of the distribution initiation transaction.
        /// </summary>
        public uint256 TransactionId { get; set; }

        /// <summary>
        /// Flag that indicates whether the reward from this initiation transaction has already been distributed.
        /// </summary>
        public bool Processed { get; set; }
    }
}
