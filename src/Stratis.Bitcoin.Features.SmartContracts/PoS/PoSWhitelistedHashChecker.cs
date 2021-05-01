using NBitcoin;
using Stratis.Bitcoin.Base.Deployments;
using Stratis.Bitcoin.Features.SmartContracts.Interfaces;
using Stratis.SmartContracts.CLR;

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
            return CheckHashWhitelisted(new uint256(hashBytes), previousHeader);
        }

        /// <summary>
        /// Checks that a supplied hash is present in the whitelisted hashes repository.
        /// </summary>
        /// <param name="hash">The hash to check.</param>
        /// <param name="previousHeader">The block before the block to check.</param>
        /// <returns>True if the hash was found in the whitelisted hashes repository.</returns>
        public bool CheckHashWhitelisted(uint256 codeHash, ChainedHeader previousHeader)
        {
            uint160 id = (new EmbeddedCodeHash(codeHash)).Id;
            return this.network.EmbeddedContractContainer.IsActive(id, previousHeader, (h, d) => this.nodeDeployments.BIP9.GetState(h, d) == ThresholdState.Active);
        }

        /// <inheritdoc />
        public bool CheckHashWhitelisted(byte[] hashBytes)
        {
            return this.CheckHashWhitelisted(hashBytes, this.chainIndexer.Tip);
        }
    }
}