﻿using System;
using NBitcoin;
using Stratis.Bitcoin.Base.Deployments;

namespace Stratis.Bitcoin.Features.SmartContracts.PoS
{
    /// <summary>
    /// The purpose of the following code is to determine the activation time with the help of the BIP9 mechanism.
    /// </summary>
    public interface ISmartContractPosActivationProvider
    {
        /// <summary>
        /// Determines smart contracts are active for a block given the previous block header.
        /// </summary>
        /// <returns>Returns the unix-style activation time.</returns>
        Func<ChainedHeader, bool> IsActive { get; set; }
    }

    /// <inheritdoc/>
    public class SmartContractPosActivationProvider : ISmartContractPosActivationProvider
    {
        private readonly Network network;
        private readonly NodeDeployments nodeDeployments;
        private readonly ChainIndexer chainIndexer;

        private readonly int deployment;
        private readonly object lockObject;

        public Func<ChainedHeader, bool> IsActive { get; set; }

        /// <summary>
        /// Constructor.
        /// </summary>
        /// <param name="network">Network.</param>
        /// <param name="nodeDeployments">Node deployments providing access to the BIP9 deployments.</param>
        /// <param name="chainIndexer">The consensus chain.</param>
        public SmartContractPosActivationProvider(Network network, NodeDeployments nodeDeployments, ChainIndexer chainIndexer)
        {
            this.network = network;
            this.nodeDeployments = nodeDeployments;
            this.chainIndexer = chainIndexer;

            this.deployment = this.network.Consensus.BIP9Deployments.FindDeploymentIndexByName("SystemContracts");

            this.lockObject = new object();

            this.IsActive = (prev) =>
            {
                lock (this.lockObject)
                {
                    return this.deployment >= 0 && this.nodeDeployments.BIP9.GetState(prev ?? this.chainIndexer.Tip, this.deployment) == ThresholdState.Active;
                }
            };
        }
    }
}