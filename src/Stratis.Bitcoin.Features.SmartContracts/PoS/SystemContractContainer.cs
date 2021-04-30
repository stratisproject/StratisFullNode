using System;
using System.Collections.Generic;
using System.Linq;
using NBitcoin;
using Stratis.Bitcoin.Utilities;
using Stratis.SmartContracts.CLR;

namespace Stratis.Bitcoin.Features.SmartContracts.PoS
{
    /// <summary>
    /// Holds the information and logic to determines whether an embedded system contract should be active.
    /// </summary>
    public class SystemContractContainer : ISystemContractContainer
    {
        private readonly Network network;

        // History of block ranges over which contracts were active.
        // The BIP9 Deployments array is sometimes cleaned up and the information therein has to be transferred here.
        private Dictionary<uint160, (int start, int? end)[]> contractActivationHistory;

        // Contracts to white (or black)-list later when the specified BIP 9 goes active.
        private Dictionary<uint160, (string, bool)> contractWhitelistingBIP9s;

        // Map key id's to contract types.
        private Dictionary<ulong, string> contractTypes;

        public PrimaryAuthenticators PrimaryAuthenticators { get; private set; }

        public SystemContractContainer(
            Network network,
            Dictionary<ulong, string> contractTypes,
            Dictionary<uint160, (int start, int? end)[]> contractActivationHistory,
            Dictionary<uint160, (string, bool)> contractWhitelistingBIP9s,
            PrimaryAuthenticators primaryAuthenticators)
        {
            this.network = network;
            this.contractTypes = contractTypes;
            this.contractActivationHistory = contractActivationHistory;
            this.contractWhitelistingBIP9s = contractWhitelistingBIP9s;
            this.PrimaryAuthenticators = primaryAuthenticators;
        }
        
        /// <summary>
        /// Extracts the contract type and version from a contract identifier.
        /// </summary>
        /// <param name="id160">Contract identifier to extract the contract type and version of.</param>
        /// <param name="contractType">Contract type as a full assembly qualified name.</param>
        /// <param name="version">Contract version.</param>
        /// <returns>The contract type and version.</returns>
        public bool TryGetContractTypeAndVersion(uint160 id160, out string contractType, out uint version)
        {
            Guard.Assert(EmbeddedContractIdentifier.IsEmbedded(id160));

            var id = new EmbeddedContractIdentifier(id160);

            version = id.Version;

            return this.contractTypes.TryGetValue(id.ContractTypeId, out contractType);
        }

        /// <inheritdoc/>
        public bool IsActive(uint160 id, ChainedHeader previousHeader, Func<ChainedHeader, int, bool> deploymentCondition)
        {
            bool isActive = false;
            if (this.contractActivationHistory.TryGetValue(id, out (int start, int? end)[] ranges))
            {
                if (ranges.Any(r => (previousHeader.Height + 1) >= r.start && (r.end == null || (previousHeader.Height + 1) <= r.end)))
                    isActive = true;
            }

            if (!isActive && this.contractWhitelistingBIP9s.TryGetValue(id, out (string deploymentName, bool whiteList) action))
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
        public IEnumerable<uint160> GetContractIdentifiers()
        {
            return this.contractActivationHistory.Keys.Concat(this.contractWhitelistingBIP9s.Keys).Distinct();
        }
    }
}
