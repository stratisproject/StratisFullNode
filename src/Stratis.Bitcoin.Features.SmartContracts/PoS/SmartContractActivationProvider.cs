using System;
using NBitcoin;
using Stratis.Bitcoin.Base.Deployments;
using Stratis.Bitcoin.Consensus;
using Stratis.Bitcoin.Consensus.Rules;

namespace Stratis.Bitcoin.Features.SmartContracts.PoS
{
    /// <summary>
    /// The purpose of the following code is to determine the activation time with the help of the BIP9 mechanism.
    /// </summary>
    public interface ISmartContractActivationProvider
    {
        /// <summary>
        /// Determines smart contracts are active for a block given the previous block header.
        /// </summary>
        /// <returns>Returns the unix-style activation time.</returns>
        Func<ChainedHeader, bool> IsActive { get; set; }

        /// <summary>
        /// Smart contract rules are not applicable until the feature has been activated.
        /// </summary>
        /// <param name="context">The rule context.</param>
        bool SkipRule(RuleContext context);
    }

    /// <inheritdoc/>
    public class SmartContractPosActivationProvider : ISmartContractActivationProvider
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

        /// <inheritdoc/>
        public bool SkipRule(RuleContext context)
        {
            if (!this.IsActive(context.ValidationContext.ChainedHeaderToValidate.Previous))
                return true;

            if (context.ValidationContext.ChainedHeaderToValidate.Header is ISmartContractBlockHeader blockHeader && !blockHeader.HasSmartContractFields)
                throw new ConsensusErrorException(new ConsensusError("bad-version", "missing smart contract block header"));

            return false;
        }
    }
}