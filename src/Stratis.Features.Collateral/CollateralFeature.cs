﻿using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Stratis.Bitcoin.Builder;
using Stratis.Bitcoin.Builder.Feature;
using Stratis.Bitcoin.Features.Miner;
using Stratis.Bitcoin.Features.PoA;
using Stratis.Bitcoin.Features.PoA.BasePoAFeatureConsensusRules;
using Stratis.Bitcoin.Features.SmartContracts;
using Stratis.Bitcoin.Features.SmartContracts.PoA;
using Stratis.Features.Collateral.CounterChain;

namespace Stratis.Features.Collateral
{
    /// <summary>
    /// Sets up the necessary components to check the collateral requirement is met on the counter chain.
    /// </summary>
    public class CollateralFeature : FullNodeFeature
    {
        private readonly ICollateralChecker collateralChecker;

        public CollateralFeature(ICollateralChecker collateralChecker)
        {
            this.collateralChecker = collateralChecker;
        }

        public override async Task InitializeAsync()
        {
            await this.collateralChecker.InitializeAsync().ConfigureAwait(false);
        }

        public override void Dispose()
        {
            this.collateralChecker?.Dispose();
        }
    }

    /// <summary>
    /// A class providing extension methods for <see cref="IFullNodeBuilder"/>.
    /// </summary>
    public static class FullNodeBuilderCollateralFeatureExtension
    {
        // Both Cirrus Peg and Cirrus Miner calls this.
        public static IFullNodeBuilder CheckForPoAMembersCollateral(this IFullNodeBuilder fullNodeBuilder, bool isMiner)
        {
            // These rules always execute between all Cirrus nodes.
            fullNodeBuilder.Network.Consensus.ConsensusRules.FullValidationRules.Insert(0, typeof(CheckCollateralCommitmentHeightRule));

            if (!isMiner)
            {
                // Remove the PoAHeaderSignatureRule if this is not a miner as CirrusD has no concept of the federation and
                // any modifications of it.
                // This rule is only required to ensure that the mining nodes don't mine on top of bad blocks. A consensus error will prevent that from happening."
                // TODO: The code should be refactored at some point so that we dont do this hack.
                var indexOf = fullNodeBuilder.Network.Consensus.ConsensusRules.FullValidationRules.IndexOf(typeof(PoAHeaderSignatureRule));
                fullNodeBuilder.Network.Consensus.ConsensusRules.FullValidationRules.RemoveAt(indexOf);
            }

            // Only configure this if the Cirrus node is a miner (CirrusPegD and CirrusMinerD)
            if (isMiner)
            {
                // Inject the CheckCollateralFullValidationRule as the first Full Validation Rule.
                // This is still a bit hacky and we need to properly review the dependencies again between the different side chain nodes.
                fullNodeBuilder.Network.Consensus.ConsensusRules.FullValidationRules.Insert(0, typeof(CheckCollateralFullValidationRule));

                fullNodeBuilder.ConfigureFeature(features =>
                {
                    features.AddFeature<CollateralFeature>()
                        .DependOn<CounterChainFeature>()
                        .DependOn<PoAFeature>()
                        .FeatureServices(services =>
                        {
                            services.AddSingleton<ICollateralChecker, CollateralChecker>();
                        });
                });
            }

            return fullNodeBuilder;
        }

        /// <summary>
        /// Adds mining to the smart contract node when on a proof-of-authority network with collateral enabled.
        /// </summary>
        public static IFullNodeBuilder UseSmartContractCollateralPoAMining(this IFullNodeBuilder fullNodeBuilder)
        {
            fullNodeBuilder.ConfigureFeature(features =>
            {
                features
                    .AddFeature<PoAFeature>()
                    .FeatureServices(services =>
                    {
                        services.AddSingleton<IFederationManager, FederationManager>();
                        services.AddSingleton<PoABlockHeaderValidator>();
                        services.AddSingleton<IPoAMiner, CollateralPoAMiner>();
                        services.AddSingleton<PoAMinerSettings>();
                        services.AddSingleton<MinerSettings>();
                        services.AddSingleton<ISlotsManager, SlotsManager>();
                        services.AddSingleton<BlockDefinition, SmartContractPoABlockDefinition>();
                        services.AddSingleton<IBlockBufferGenerator, BlockBufferGenerator>();
                    });
            });

            return fullNodeBuilder;
        }
    }
}
