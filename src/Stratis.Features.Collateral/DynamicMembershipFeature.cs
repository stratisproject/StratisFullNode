using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using NBitcoin;
using Stratis.Bitcoin.Builder;
using Stratis.Bitcoin.Builder.Feature;
using Stratis.Bitcoin.Features.Collateral.ConsensusRules;
using Stratis.Bitcoin.Features.Miner;
using Stratis.Bitcoin.Features.PoA;
using Stratis.Bitcoin.Features.SmartContracts;
using Stratis.Bitcoin.Features.SmartContracts.PoA;
using Stratis.Bitcoin.Utilities;
using Stratis.Features.Collateral.CounterChain;

namespace Stratis.Features.Collateral
{
    /// <summary>
    /// Sets up the necessary components to check the collateral requirement is met on the counter chain.
    /// </summary>
    public class DynamicMembershipFeature : FullNodeFeature
    {
        private readonly JoinFederationRequestMonitor joinFederationRequestMonitor;
        private readonly Network network;

        public DynamicMembershipFeature(JoinFederationRequestMonitor joinFederationRequestMonitor, Network network)
        {
            this.joinFederationRequestMonitor = joinFederationRequestMonitor;
            this.network = network;
        }

        public override async Task InitializeAsync()
        {
            var options = (PoAConsensusOptions)this.network.Consensus.Options;
            if (options.VotingEnabled)
                await this.joinFederationRequestMonitor.InitializeAsync();
        }

        public override void Dispose()
        {
        }
    }

    /// <summary>
    /// A class providing extension methods for <see cref="IFullNodeBuilder"/>.
    /// </summary>
    public static class FullNodeBuilderDynamicMembershipFeatureExtension
    {
        // Both Cirrus Peg and Cirrus Miner calls this.
        public static IFullNodeBuilder AddDynamicMemberhip(this IFullNodeBuilder fullNodeBuilder)
        {
            Guard.Assert(fullNodeBuilder.Network.Consensus.ConsensusFactory is CollateralPoAConsensusFactory);

            fullNodeBuilder.Network.Consensus.MempoolRules.Add(typeof(VotingRequestValidationRule));
            fullNodeBuilder.Network.Consensus.ConsensusRules.PartialValidationRules.Add(typeof(MandatoryCollateralMemberVotingRule));

            fullNodeBuilder.ConfigureFeature(features =>
            {
                features.AddFeature<DynamicMembershipFeature>()
                    .DependOn<CounterChainFeature>()
                    .DependOn<PoAFeature>()
                    .FeatureServices(services =>
                    {
                        services.AddSingleton<IFederationManager, CollateralFederationManager>();
                        services.AddSingleton<JoinFederationRequestMonitor>();
                    });
            });

            return fullNodeBuilder;
        }
    }
}
