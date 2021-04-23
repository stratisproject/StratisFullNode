using NBitcoin;
using Stratis.Bitcoin.Base.Deployments;
using Stratis.Bitcoin.Features.SmartContracts.Interfaces;

namespace Stratis.Bitcoin.Features.SmartContracts.PoS
{
    /// <summary>
    /// Checks hashes against a whitelist.
    /// </summary>
    public class PoSWhitelistedHashChecker : IWhitelistedHashChecker
    {
        private readonly Network network;
        private readonly NodeDeployments nodeDeployments;
        private readonly ChainIndexer chainIndexer;

        /// <summary>
        /// Checks for whitelisted contracts on PoS networks.
        /// </summary>
        public PoSWhitelistedHashChecker(Network network, NodeDeployments nodeDeployments, ChainIndexer chainIndexer)
        {
            this.network = network;
            this.nodeDeployments = nodeDeployments;
            this.chainIndexer = chainIndexer;
        }

        /// <summary>
        /// Checks that a supplied hash is present in the whitelisted hashes repository.
        /// </summary>
        /// <param name="hashBytes">The bytes of the hash to check.</param>
        /// <param name="previousHeader">The block before the block to check.</param>
        /// <returns>True if the hash was found in the whitelisted hashes repository.</returns>
        public bool CheckHashWhitelisted(byte[] hashBytes, ChainedHeader previousHeader)
        {
            if (hashBytes.Length != 32)
            {
                // For this implementation, only 32 byte wide hashes are accepted.
                return false;
            }

            var hash = new uint256(hashBytes);

            return this.network.SystemContractContainer.IsActive(hash, previousHeader, (h, d) => this.nodeDeployments.BIP9.GetState(h, d) == ThresholdState.Active);
        }

        /// <inheritdoc />
        public bool CheckHashWhitelisted(byte[] hashBytes)
        {
            return this.CheckHashWhitelisted(hashBytes, this.chainIndexer.Tip);
        }
    }
}