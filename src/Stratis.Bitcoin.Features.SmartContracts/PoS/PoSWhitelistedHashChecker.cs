using System.Collections.Generic;
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
        // Currently white-listed contracts.
        private static HashSet<uint256> currentWhiteListedHashes = new HashSet<uint256>()
        {
            uint256.Zero
        };

        // Contracts to white-list later when the specified BIP 9 goes active.
        private static Dictionary<uint256, string> contractWhitelistingBIP9s = new Dictionary<uint256, string>() {
            { uint256.Zero, "SystemContracts" }
        };

        // Contracts to black-list later when the specified BIP 9 goes active.
        private static Dictionary<uint256, string> contractBlacklistingBIP9s = new Dictionary<uint256, string>() {
            { uint256.Zero, "SystemContracts" }
        };

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

            bool isWhiteListed = currentWhiteListedHashes.Contains(hash);

            if (!isWhiteListed && contractWhitelistingBIP9s.TryGetValue(hash, out string deploymentName))
            {
                // Found.
            }
            else if (isWhiteListed && contractBlacklistingBIP9s.TryGetValue(hash, out deploymentName))
            {
                // Found.
            }
            else
            {
                return isWhiteListed;
            }

            int deployment = this.network.Consensus.BIP9Deployments.FindDeploymentIndexByName(deploymentName);
            if (deployment < 0)
                return isWhiteListed;

            if (this.nodeDeployments.BIP9.GetState(previousHeader, deployment) == ThresholdState.Active)
                isWhiteListed = !isWhiteListed;

            return isWhiteListed;
        }

        /// <inheritdoc />
        public bool CheckHashWhitelisted(byte[] hashBytes)
        {
            return this.CheckHashWhitelisted(hashBytes, this.chainIndexer.Tip);
        }
    }
}