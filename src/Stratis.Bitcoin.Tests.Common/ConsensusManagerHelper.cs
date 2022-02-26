using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
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
            IConsensusRuleEngine consensusRules = null)
        {
            string[] param = dataDir == null ? new string[] { } : new string[] { $"-datadir={dataDir}" };

            var nodeSettings = new NodeSettings(network, args: param);

            network.Consensus.Options = new ConsensusOptions();

            // Dont check PoW of a header in this test.
            network.Consensus.ConsensusRules.HeaderValidationRules.RemoveAll(x => x == typeof(CheckDifficultyPowRule));

            var mockingContext = new MockingContext()
                .AddService(network)
                .AddService(nodeSettings)
                .AddService(nodeSettings.DataFolder)
                .AddService(nodeSettings.LoggerFactory)
                .AddService(DateTimeProvider.Default)
                .AddService<INodeLifetime>(typeof(NodeLifetime))
                .AddService<IVersionProvider>(typeof(VersionProvider))
                .AddService<INodeStats>(typeof(NodeStats))
                .AddService<ISignals>(typeof(Signals.Signals))
                .AddService<ISelfEndpointTracker>(typeof(SelfEndpointTracker))
                .AddService<IPeerAddressManager>(typeof(PeerAddressManager))
                .AddService(new PayloadProvider().DiscoverPayloads())
                .AddService<INetworkPeerFactory>(typeof(NetworkPeerFactory))
                .AddService<IPeerDiscovery>(typeof(PeerDiscovery))
                .AddService(chainIndexer ?? new ChainIndexer(network))
                .AddService<ICoinView>(ctx => inMemoryCoinView ?? new InMemoryCoinView(new HashHeightPair(ctx.GetService<ChainIndexer>().Tip)))
                .AddService<IChainState>(chainState ?? new ChainState())
                .AddService<IConnectionManager>(typeof(ConnectionManager))
                .AddService<IPeerBanning>(typeof(PeerBanning))
                .AddService<ICheckpoints>(typeof(Checkpoints))
                .AddService<IInvalidBlockHashStore>(typeof(InvalidBlockHashStore))
                .AddService<PowConsensusRuleEngine>()
                .AddService(ctx => consensusRules ?? ctx.GetService<PowConsensusRuleEngine>())
                .AddService<IIntegrityValidator>(typeof(IntegrityValidator))
                .AddService<IPartialValidator>(typeof(PartialValidator))
                .AddService<IFullValidator>(typeof(FullValidator))
                .AddService<IHeaderValidator>(typeof(HeaderValidator))
                .AddService<IChainWorkComparer>(typeof(ChainWorkComparer))
                .AddService<IChainedHeaderTree>(typeof(ChainedHeaderTree))
                .AddService<IConsensusManager>(typeof(ConsensusManager).GetConstructors(BindingFlags.Instance | BindingFlags.NonPublic)[0]);

            return mockingContext.GetService<IConsensusManager>();
        }
    }
}
