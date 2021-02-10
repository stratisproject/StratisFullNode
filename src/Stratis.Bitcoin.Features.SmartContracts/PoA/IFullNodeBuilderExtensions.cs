using Microsoft.Extensions.DependencyInjection;
using Stratis.Bitcoin.Builder;
using Stratis.Bitcoin.Configuration.Logging;
using Stratis.Bitcoin.Consensus;
using Stratis.Bitcoin.Features.Consensus;
using Stratis.Bitcoin.Features.Consensus.CoinViews;
using Stratis.Bitcoin.Features.PoA;
using Stratis.Bitcoin.Features.PoA.Voting;
using Stratis.Bitcoin.Features.SmartContracts.ReflectionExecutor.Consensus.Rules;
using Stratis.Bitcoin.Features.SmartContracts.Rules;

namespace Stratis.Bitcoin.Features.SmartContracts.PoA
{
    public static partial class IFullNodeBuilderExtensions
    {
        /// <summary>
        /// Adds common PoA functionality to the side chain node.
        /// </summary>
        public static IFullNodeBuilder AddPoAFeature(this IFullNodeBuilder fullNodeBuilder)
        {
            fullNodeBuilder.ConfigureFeature(features =>
            {
                features
                    .AddFeature<PoAFeature>()
                    .FeatureServices(services =>
                    {
                        // Voting & Polls 
                        services.AddSingleton<IFederationHistory, FederationHistory>();
                        services.AddSingleton<VotingManager>();
                        services.AddSingleton<IWhitelistedHashesRepository, WhitelistedHashesRepository>();
                        services.AddSingleton<IPollResultExecutor, PollResultExecutor>();
                        services.AddSingleton<IIdleFederationMembersKicker, IdleFederationMembersKicker>();

                        // Federation Awareness
                        services.AddSingleton<IFederationManager, FederationManager>();
                        services.AddSingleton<ISlotsManager, SlotsManager>();

                        // Rule Related
                        services.AddSingleton<PoABlockHeaderValidator>();
                    });
            });

            return fullNodeBuilder;
        }

        /// <summary>
        /// Configures the side chain node with the PoA consensus rule engine.
        /// </summary>
        public static IFullNodeBuilder UsePoAConsensus(this IFullNodeBuilder fullNodeBuilder, DbType dbType = DbType.Leveldb)
        {
            LoggingConfiguration.RegisterFeatureNamespace<ConsensusFeature>("consensus");

            fullNodeBuilder.ConfigureFeature(features =>
            {
                features
                    .AddFeature<ConsensusFeature>()
                    .DependOn<PoAFeature>()
                    .FeatureServices(services =>
                    {
                        services.ConfigureCoinDatabaseImplementation(dbType);
                        services.AddSingleton(typeof(IContractTransactionPartialValidationRule), typeof(SmartContractFormatLogic));
                        services.AddSingleton<IConsensusRuleEngine, PoAConsensusRuleEngine>();
                        services.AddSingleton<ICoinView, CachedCoinView>();
                    });
            });

            return fullNodeBuilder;
        }
    }
}
