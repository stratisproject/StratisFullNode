﻿using System;
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
    public class EmbeddedContractVersion
    {
        public EmbeddedContractVersion(Type contractType, ulong version, (int start, int? end)[] activationHistory, string activationName, bool activationState)
        {
            this.ContractType = contractType;
            this.Version = version;
            this.ActivationHistory = activationHistory;
            this.ActivationName = activationName;
            this.ActivationState = activationState;
        }

        /// <summary>The contract version that this information applies to.</summary>
        public ulong Version { get; private set; }

        /// <summary>The <see cref="Type.AssemblyQualifiedName"/> of the contract.</summary>
        public Type ContractType { get; private set; }

        /// <summary>The aadress of the contract.</summary>
        public uint160 Address => new EmbeddedContractIdentifier(typeof(Authentication), 1);

        /// <summary>History of block ranges over which contracts were active.
        /// The BIP9 Deployments array is sometimes cleaned up and the information therein has to be transferred here.</summary>
        public (int start, int? end)[] ActivationHistory { get; private set; }

        /// <summary>Name of BIP9 activation to monitor.</summary>
        public string ActivationName { get; private set; }

        /// <summary>Whether to white (or black)-list later when the monitored BIP 9 goes active.</summary>
        public bool ActivationState { get; private set; }
    }

    /// <summary>
    /// Holds the information and logic to determines whether an embedded system contract should be active.
    /// </summary>
    public class EmbeddedContractContainer : IEmbeddedContractContainer
    {
        private readonly Network network;

        /// <summary>The embedded contracts for this network.</summary>
        private Dictionary<uint160, EmbeddedContractVersion> contracts;

        /// <summary>
        /// The addresses (defaults) and quorum of the primary authenticators of this network.
        /// </summary>
        public PrimaryAuthenticators PrimaryAuthenticators { get; private set; }

        /// <summary>The class constructor.</summary>
        public EmbeddedContractContainer(
            Network network,
            List<EmbeddedContractVersion> contracts,
            PrimaryAuthenticators primaryAuthenticators)
        {
            this.network = network;
            this.contracts = new Dictionary<uint160, EmbeddedContractVersion>();
            foreach (EmbeddedContractVersion contract in contracts)
                this.contracts.Add(contract.Address, contract);
            this.PrimaryAuthenticators = primaryAuthenticators;
        }

        /// <inheritdoc/>
        public bool TryGetContractTypeAndVersion(uint160 id, out string contractType, out uint version)
        {
            Guard.Assert(EmbeddedContractIdentifier.IsEmbedded(id));

            version = new EmbeddedContractIdentifier(id).Version;

            if (!this.contracts.TryGetValue(id, out EmbeddedContractVersion contract))
            {
                contractType = null;
                return false;
            }

            contractType = contract.ContractType.AssemblyQualifiedName;

            return true;
        }

        /// <inheritdoc/>
        public bool IsActive(uint160 id, ChainedHeader previousHeader, Func<ChainedHeader, int, bool> deploymentCondition)
        {
            if (!this.contracts.TryGetValue(id, out EmbeddedContractVersion contract))
                return false;

            bool isActive = contract.ActivationHistory.Any(r => (previousHeader.Height + 1) >= r.start && (r.end == null || (previousHeader.Height + 1) <= r.end));

            int deployment = this.network.Consensus.BIP9Deployments.FindDeploymentIndexByName(contract.ActivationName);
            if (deployment < 0)
                return isActive;

            if (deploymentCondition(previousHeader, deployment))
                isActive = contract.ActivationState;

            return isActive;
        }

        /// <inheritdoc/>
        public IEnumerable<uint160> GetContractIdentifiers()
        {
            return this.contracts.Select(c => c.Key);
        }
    }
}
