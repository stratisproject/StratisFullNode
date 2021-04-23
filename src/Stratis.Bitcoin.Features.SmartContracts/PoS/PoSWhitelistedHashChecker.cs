using System;
using System.Collections.Generic;
using System.Linq;
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
        // TODO: Move these variables to the respective main, test / regtest network classes.

        private static byte[] pseudoHashSignature = new byte[8] { 0, 0, 0, 0, 0, 0, 0, 0 };

        // Pseudo-hash consisting of 8 "signature", 20 "address" + 4 "version" bytes.
        private static uint256 PseudoHash(KeyId address, uint version)
        {
            byte[] hashBytes = pseudoHashSignature.Concat(address.ToBytes()).Concat(BitConverter.GetBytes(version)).ToArray();
            return new uint256(hashBytes);
        }

        // History of block ranges over which contracts were active.
        // The BIP9 Deployments array is sometimes cleaned up and the information therein has to be transferred here.
        private static Dictionary<uint256, (int start, int? end)[]> contractActivationHistory = new Dictionary<uint256, (int, int?)[]>()
        {
            { PseudoHash(new KeyId(1), 0), new (int, int?)[] { (0, 0) } }
        };

        // Contracts to white (or black)-list later when the specified BIP 9 goes active.
        private static Dictionary<uint256, (string, bool)> contractWhitelistingBIP9s = new Dictionary<uint256, (string, bool)>() {
            { PseudoHash(new KeyId(1), 0), ("SystemContracts", true) }
        };

        private static Dictionary<uint160, Type> contractTypes = new Dictionary<uint160, Type>() {
            // { new KeyId(1), typeof(SystemContractDictionary) }
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

            bool isWhiteListed = false;
            if (contractActivationHistory.TryGetValue(hash, out (int start, int? end)[] ranges))
            {
                if (ranges.Any(r => (previousHeader.Height + 1) >= r.start && (r.end == null || (previousHeader.Height + 1) <= r.end)))
                    isWhiteListed = true;
            }

            if (!isWhiteListed && contractWhitelistingBIP9s.TryGetValue(hash, out (string deploymentName, bool whiteList) action))
            {
                int deployment = this.network.Consensus.BIP9Deployments.FindDeploymentIndexByName(action.deploymentName);
                if (deployment < 0)
                    return isWhiteListed;

                if (this.nodeDeployments.BIP9.GetState(previousHeader, deployment) == ThresholdState.Active)
                    isWhiteListed = action.whiteList;
            }

            return isWhiteListed;
        }

        /// <inheritdoc />
        public bool CheckHashWhitelisted(byte[] hashBytes)
        {
            return this.CheckHashWhitelisted(hashBytes, this.chainIndexer.Tip);
        }
    }
}