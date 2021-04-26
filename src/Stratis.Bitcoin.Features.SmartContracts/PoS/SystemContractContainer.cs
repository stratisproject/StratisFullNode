using System;
using System.Collections.Generic;
using System.Linq;
using NBitcoin;
using Stratis.Bitcoin.Utilities;

namespace Stratis.Bitcoin.Features.SmartContracts.PoS
{
    /// <summary>
    /// Holds the information and logic to determines whether an embedded system contract should be active.
    /// </summary>
    public class SystemContractContainer : ISystemContractContainer
    {
        private static byte[] pseudoHashSignature = new byte[8] { 0, 0, 0, 0, 0, 0, 0, 0 };

        private readonly Network network;

        // History of block ranges over which contracts were active.
        // The BIP9 Deployments array is sometimes cleaned up and the information therein has to be transferred here.
        private Dictionary<uint256, (int start, int? end)[]> contractActivationHistory;

        // Contracts to white (or black)-list later when the specified BIP 9 goes active.
        private Dictionary<uint256, (string, bool)> contractWhitelistingBIP9s;

        // Map key id's to contract types.
        private Dictionary<KeyId, string> contractTypes;

        public SystemContractContainer(
            Network network,
            Dictionary<KeyId, string> contractTypes,
            Dictionary<uint256, (int start, int? end)[]> contractActivationHistory,
            Dictionary<uint256, (string, bool)> contractWhitelistingBIP9s)
        {
            this.network = network;
            this.contractTypes = contractTypes;
            this.contractActivationHistory = contractActivationHistory;
            this.contractWhitelistingBIP9s = contractWhitelistingBIP9s;
        }

        /// <summary>
        ///  Pseudo-hash consisting of 8 "signature", 20 "contractTypeId" + 4 "version" bytes.
        /// </summary>
        /// <param name="contractTypeId"><see cref="KeyId"/> that will be mapped to a contract class.</param>
        /// <param name="version">Version that will be passed to contract constructor.</param>
        /// <returns>Pseudo-hash identifying the system contract type and version.</returns>
        public static uint256 GetPseudoHash(KeyId contractTypeId, uint version)
        {
            byte[] hashBytes = pseudoHashSignature.Concat(contractTypeId.ToBytes()).Concat(BitConverter.GetBytes(version)).ToArray();
            return new uint256(hashBytes);
        }

        /// <summary>
        /// Determines whether the hash is a "pseudo hash".
        /// </summary>
        /// <param name="hash">Hash to evaluate.</param>
        /// <returns><c>True</c> if its a pseudo-hash and <c>flase</c> otherwise.</returns>
        public static bool IsPseudoHash(uint256 hash)
        {
            return hash.GetLow64() == BitConverter.ToUInt64(pseudoHashSignature);
        }

        /// <summary>
        /// Extracts the key id and version from a pseudo-hash.
        /// </summary>
        /// <param name="hash">The hash to extract the key id and version of.</param>
        /// <returns>The key id and version.</returns>
        public (string contractType, uint version) GetContractTypeAndVersion(uint256 hash)
        {
            Guard.Assert(IsPseudoHash(hash));

            var hashBytes = hash.ToBytes();

            var keyIdBytes = new byte[20];
            Array.Copy(hashBytes, 8, keyIdBytes, 0, 20);

            return (this.contractTypes[new KeyId(keyIdBytes)], BitConverter.ToUInt32(hashBytes, 28));
        }

        /// <inheritdoc/>
        public bool IsActive(uint256 hash, ChainedHeader previousHeader, Func<ChainedHeader, int, bool> deploymentCondition)
        {
            bool isActive = false;
            if (this.contractActivationHistory.TryGetValue(hash, out (int start, int? end)[] ranges))
            {
                if (ranges.Any(r => (previousHeader.Height + 1) >= r.start && (r.end == null || (previousHeader.Height + 1) <= r.end)))
                    isActive = true;
            }

            if (!isActive && this.contractWhitelistingBIP9s.TryGetValue(hash, out (string deploymentName, bool whiteList) action))
            {
                int deployment = this.network.Consensus.BIP9Deployments.FindDeploymentIndexByName(action.deploymentName);
                if (deployment < 0)
                    return isActive;

                if (deploymentCondition(previousHeader, deployment))
                    isActive = action.whiteList;
            }

            return isActive;
        }

        /// <inheritdoc/>
        public IEnumerable<uint256> GetContractHashes()
        {
            return this.contractActivationHistory.Keys.Concat(this.contractWhitelistingBIP9s.Keys).Distinct();
        }
    }
}
