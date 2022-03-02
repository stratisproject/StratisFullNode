using System;
using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using NBitcoin;
using Stratis.Bitcoin.Base;
using Stratis.Bitcoin.Configuration;
using Stratis.Bitcoin.Connection;
using Stratis.Bitcoin.Consensus;
using Stratis.Bitcoin.Consensus.Validators;
using Stratis.Bitcoin.Features.Consensus.CoinViews;
using Stratis.Bitcoin.Features.Consensus.Rules;
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
            var mockingServices = GetMockingServices(network, dataDir,
                ctx => chainState,
                ctx => inMemoryCoinView ?? new InMemoryCoinView(new HashHeightPair(ctx.GetService<ChainIndexer>().Tip)),
                ctx => chainIndexer,
                ctx => consensusRules ?? ctx.GetService<PowConsensusRuleEngine>());

            mockingServices.AddSingleton(finalizedBlockInfoRepository ?? new FinalizedBlockInfoRepository(new HashHeightPair()));
            
            return new MockingContext(mockingServices).GetService<IConsensusManager>();
        }

        public static IServiceCollection GetMockingServices(
            Network network,
            string dataDir = null,
            Func<IServiceProvider, IChainState> chainState = null,
            Func<IServiceProvider, ICoinView> coinView = null,
            Func<IServiceProvider, ChainIndexer> chainIndexer = null,
            Func<IServiceProvider, IConsensusRuleEngine> consensusRules = null)
        {
            string[] param = dataDir == null ? new string[] { } : new string[] { $"-datadir={dataDir}" };

            var nodeSettings = new NodeSettings(network, args: param);

            network.Consensus.Options = new ConsensusOptions();

            // Dont check PoW of a header in this test.
            network.Consensus.ConsensusRules.HeaderValidationRules.RemoveAll(x => x == typeof(CheckDifficultyPowRule));

            var mockingServices = new ServiceCollection()
                .AddSingleton(network)
                .AddSingleton(nodeSettings)
                .AddSingleton(nodeSettings.DataFolder)
                .AddSingleton(nodeSettings.LoggerFactory)
                .AddSingleton(DateTimeProvider.Default)
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
                .AddSingleton(coinView ?? (ctx => ctx.GetService<Mock<ICoinView>>().Object))
                .AddSingleton(chainState ?? (ctx => new ChainState()))
                .AddSingleton<IConnectionManager, ConnectionManager>()
                .AddSingleton<IPeerBanning, PeerBanning>()
                .AddSingleton<ICheckpoints, Checkpoints>()
                .AddSingleton<IInvalidBlockHashStore, InvalidBlockHashStore>()
                .AddSingleton(consensusRules ?? (ctx => ctx.GetService<Mock<IConsensusRuleEngine>>().Object))
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
