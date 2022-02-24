using System.Reflection;
using NBitcoin;
using Stratis.Bitcoin.Base;
using Stratis.Bitcoin.Configuration;
using Stratis.Bitcoin.Configuration.Settings;
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
        public static ConsensusManager CreateConsensusManager(
            Network network,
            string dataDir = null,
            ChainState chainState = null,
            InMemoryCoinView inMemoryCoinView = null,
            ChainIndexer chainIndexer = null,
            ConsensusRuleEngine consensusRules = null)
        {
            string[] param = dataDir == null ? new string[] { } : new string[] { $"-datadir={dataDir}" };

            var nodeSettings = new NodeSettings(network, args: param);

            network.Consensus.Options = new ConsensusOptions();

            // Dont check PoW of a header in this test.
            network.Consensus.ConsensusRules.HeaderValidationRules.RemoveAll(x => x == typeof(CheckDifficultyPowRule));

            var consensusSettings = new ConsensusSettings(nodeSettings);

            if (chainIndexer == null)
                chainIndexer = new ChainIndexer(network);

            if (inMemoryCoinView == null)
                inMemoryCoinView = new InMemoryCoinView(new HashHeightPair(chainIndexer.Tip));

            if (chainState == null)
                chainState = new ChainState();

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
                .AddService(new PeerConnector[] { })
                .AddService(chainIndexer)
                .AddService<ICoinView>(inMemoryCoinView)
                .AddService<IChainState>(chainState)
                .AddService<IConnectionManager>(typeof(ConnectionManager))
                .AddService<IPeerBanning>(typeof(PeerBanning))
                .AddService<ICheckpoints>(typeof(Checkpoints).GetConstructor(new[] { typeof(Network), typeof(ConsensusSettings) }))
                .AddService<IInvalidBlockHashStore>(typeof(InvalidBlockHashStore));

            if (consensusRules == null)
                consensusRules = mockingContext.GetService<IConsensusRuleEngine>(typeof(PowConsensusRuleEngine), addIfNotExists: true).SetupRulesEngineParent();
            else
                mockingContext.AddService<IConsensusRuleEngine>(consensusRules);

            mockingContext
                .AddService<IIntegrityValidator>(typeof(IntegrityValidator))
                .AddService<IPartialValidator>(typeof(PartialValidator))
                .AddService<IFullValidator>(typeof(FullValidator))
                .AddService<IHeaderValidator>(typeof(HeaderValidator))
                .AddService<IChainWorkComparer>(typeof(ChainWorkComparer))
                .AddService<IChainedHeaderTree>(typeof(ChainedHeaderTree));

            mockingContext.AddService<ConsensusManager>(typeof(ConsensusManager).GetConstructors(BindingFlags.Instance | BindingFlags.NonPublic)[0]);

            return mockingContext.GetService<ConsensusManager>();
        }
    }
}
