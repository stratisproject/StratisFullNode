using Microsoft.Extensions.DependencyInjection;
using Stratis.Bitcoin.Builder;
using Stratis.Bitcoin.Configuration.Logging;
using Stratis.Bitcoin.Consensus;
using Stratis.Bitcoin.Features.Consensus;
using Stratis.Bitcoin.Features.Consensus.CoinViews;
using Stratis.Bitcoin.Features.Miner;
using Stratis.Bitcoin.Features.PoA;
using Stratis.Bitcoin.Features.PoA.Voting;
using Stratis.Bitcoin.Features.SmartContracts.ReflectionExecutor.Consensus.Rules;
using Stratis.Bitcoin.Features.SmartContracts.Rules;

namespace Stratis.Bitcoin.Features.SmartContracts.PoA
{
    public static partial class IFullNodeBuilderExtensions
    {
        /// <summary>
        /// Configures the node with the smart contract proof of authority consensus model.
        /// </summary>
        public static IFullNodeBuilder UseSmartContractPoAConsensus(this IFullNodeBuilder fullNodeBuilder, DbType coindbType = DbType.Leveldb)
        {
            LoggingConfiguration.RegisterFeatureNamespace<ConsensusFeature>("consensus");

            fullNodeBuilder.ConfigureFeature(features =>
            {
                features
                    .AddFeature<ConsensusFeature>()
                    .DependOn<SmartContractFeature>()
                    .FeatureServices(services =>
                    {
                        AddCoindbImplementation(services, coindbType);
                        services.AddSingleton<ICoinView, CachedCoinView>();
                        services.AddSingleton<VotingManager>();
                        services.AddSingleton<IWhitelistedHashesRepository, WhitelistedHashesRepository>();
                        services.AddSingleton<IPollResultExecutor, PollResultExecutor>();

                        services.AddSingleton(typeof(IContractTransactionPartialValidationRule), typeof(SmartContractFormatLogic));
                        services.AddSingleton<IConsensusRuleEngine, PoAConsensusRuleEngine>();

                        // Voting.
                        services.AddSingleton<VotingManager>();
                        services.AddSingleton<IPollResultExecutor, PollResultExecutor>();
                        services.AddSingleton<IWhitelistedHashesRepository, WhitelistedHashesRepository>();
                        services.AddSingleton<IIdleFederationMembersKicker, IdleFederationMembersKicker>();
                    });
            });

            return fullNodeBuilder;
        }

        private static void AddCoindbImplementation(IServiceCollection services, DbType coindbType)
        {
            if (coindbType == DbType.Dbreeze)
                services.AddSingleton<ICoindb, DBreezeCoindb>();

            if (coindbType == DbType.Leveldb)
                services.AddSingleton<ICoindb, LeveldbCoindb>();

            if (coindbType == DbType.Faster)
                services.AddSingleton<ICoindb, FasterCoindb>();
        }

        /// <summary>
        /// Adds mining to the smart contract node when on a proof-of-authority network.
        /// </summary>
        public static IFullNodeBuilder UseSmartContractPoAMining(this IFullNodeBuilder fullNodeBuilder, bool isMiner = false)
        {
            fullNodeBuilder.ConfigureFeature(features =>
            {
                features
                    .AddFeature<PoAFeature>()
                    .FeatureServices(services =>
                    {
                        services.AddSingleton<IFederationManager, FederationManager>();
                        services.AddSingleton<PoABlockHeaderValidator>();
                        services.AddSingleton<ISlotsManager, SlotsManager>();
                        services.AddSingleton<IBlockBufferGenerator, BlockBufferGenerator>();

                        if (isMiner)
                        {
                            services.AddSingleton<IPoAMiner, PoAMiner>();
                            services.AddSingleton<PoAMinerSettings>();
                            services.AddSingleton<MinerSettings>();
                            services.AddSingleton<BlockDefinition, SmartContractPoABlockDefinition>();
                        }
                    });
            });

            return fullNodeBuilder;
        }
    }
}
