using System;
using NBitcoin;
using Stratis.Bitcoin.Base.Deployments;
using Stratis.Bitcoin.Utilities;

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

        private readonly int deployment;
        private readonly object lockObject;

        public Func<ChainedHeader, bool> IsActive { get; set; }

        /// <summary>
        /// Constructor.
        /// </summary>
        /// <param name="network">Network.</param>
        /// <param name="nodeDeployments">Node deployments providing access to the BIP9 deployments.</param>
        public SmartContractPosActivationProvider(Network network, NodeDeployments nodeDeployments)
        {
            this.network = network;
            this.nodeDeployments = nodeDeployments;

            this.deployment = this.network.Consensus.BIP9Deployments.FindDeploymentIndexByName("SystemContracts");
            Guard.Assert(this.deployment >= 0);

            this.lockObject = new object();

            this.IsActive = (prev) =>
            {
                lock (this.lockObject)
                {
                    return this.nodeDeployments.BIP9.GetState(prev, this.deployment) == ThresholdState.Active;
                }
            };
        }
    }
}