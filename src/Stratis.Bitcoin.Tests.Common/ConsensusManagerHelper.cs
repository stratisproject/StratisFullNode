using System;
using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using NBitcoin;
using Stratis.Bitcoin.AsyncWork;
using Stratis.Bitcoin.Base;
using Stratis.Bitcoin.Configuration;
using Stratis.Bitcoin.Connection;
using Stratis.Bitcoin.Consensus;
using Stratis.Bitcoin.Consensus.Validators;
using Stratis.Bitcoin.Features.Consensus.CoinViews;
using Stratis.Bitcoin.Features.Consensus.Rules.CommonRules;
using Stratis.Bitcoin.Interfaces;
using Stratis.Bitcoin.P2P;
using Stratis.Bitcoin.P2P.Peer;
using Stratis.Bitcoin.P2P.Protocol.Payloads;
using Stratis.Bitcoin.Signals;
using Stratis.Bitcoin.Utilities;

namespace Stratis.Bitcoin.Tests.Common
{
    public static class ConsensusManagerHelper
    {
        public static IConsensusManager CreateConsensusManager(
            Network network,
            string dataDir = null,
            ChainState chainState = null,
            InMemoryCoinView inMemoryCoinView = null,
            ChainIndexer chainIndexer = null,
            IConsensusRuleEngine consensusRules = null,
            IFinalizedBlockInfoRepository finalizedBlockInfoRepository = null)
        {
            IServiceCollection mockingServices = GetMockingServices(network,
                ctx => new NodeSettings(network, args: ((dataDir == null) ? new string[] { } : new string[] { $"-datadir={dataDir}" })),
                ctx => chainState,
                ctx => chainIndexer);

            if (consensusRules != null)
                mockingServices.AddSingleton(consensusRules);

            if (inMemoryCoinView != null)
            {
                mockingServices.AddSingleton<ICoinView>(inMemoryCoinView);
                mockingServices.AddSingleton<ICoindb>(inMemoryCoinView);
            }

            mockingServices.AddSingleton(finalizedBlockInfoRepository ?? new FinalizedBlockInfoRepository(new HashHeightPair()));

            return new MockingContext(mockingServices).GetService<IConsensusManager>();
        }

        public static IServiceCollection GetMockingServices(
            Network network,
            Func<IServiceProvider, NodeSettings> nodeSettings = null,
            Func<IServiceProvider, IChainState> chainState = null,
            Func<IServiceProvider, ChainIndexer> chainIndexer = null)
        {
            network.Consensus.Options = new ConsensusOptions();

            // Dont check PoW of a header in this test.
            network.Consensus.ConsensusRules.HeaderValidationRules.RemoveAll(x => x == typeof(CheckDifficultyPowRule));

            var mockingServices = new ServiceCollection()
                .AddSingleton(network)
                .AddSingleton(nodeSettings ?? (ctx => new NodeSettings(network)))
                .AddSingleton(ctx => ctx.GetService<NodeSettings>().DataFolder)
                .AddSingleton(ctx => ctx.GetService<NodeSettings>().LoggerFactory)
                .AddSingleton(DateTimeProvider.Default)
                .AddSingleton<IAsyncProvider, AsyncProvider>()
                .AddSingleton<INodeLifetime, NodeLifetime>()
                .AddSingleton<IVersionProvider, VersionProvider>()
                .AddSingleton<INodeStats, NodeStats>()
                .AddSingleton<ISignals, Signals.Signals>()
                .AddSingleton<ISelfEndpointTracker, SelfEndpointTracker>()
                .AddSingleton<IPeerAddressManager, PeerAddressManager>()
                .AddSingleton(new PayloadProvider().DiscoverPayloads())
                .AddSingleton<INetworkPeerFactory, NetworkPeerFactory>()
                .AddSingleton<IPeerDiscovery, PeerDiscovery>()
                .AddSingleton(chainIndexer ?? (ctx => new ChainIndexer(network)))
                .AddSingleton(chainState ?? (ctx => new ChainState()))
                .AddSingleton<IConnectionManager, ConnectionManager>()
                .AddSingleton<IPeerBanning, PeerBanning>()
                .AddSingleton<ICheckpoints, Checkpoints>()
                .AddSingleton<IInvalidBlockHashStore, InvalidBlockHashStore>()
                .AddSingleton<IIntegrityValidator, IntegrityValidator>()
                .AddSingleton<IPartialValidator, PartialValidator>()
                .AddSingleton<IFullValidator, FullValidator>()
                .AddSingleton<IHeaderValidator, HeaderValidator>()
                .AddSingleton<IChainWorkComparer, ChainWorkComparer>()
                .AddSingleton<IChainedHeaderTree, ChainedHeaderTree>()
                .AddSingleton<IConsensusManager>(typeof(ConsensusManager).GetConstructors(BindingFlags.Instance | BindingFlags.NonPublic)[0]);

            return mockingServices;
        }
    }
}
