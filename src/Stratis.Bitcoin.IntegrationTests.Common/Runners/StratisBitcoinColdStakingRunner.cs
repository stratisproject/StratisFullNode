using NBitcoin;
using NBitcoin.Protocol;
using Stratis.Bitcoin.Base;
using Stratis.Bitcoin.Builder;
using Stratis.Bitcoin.Configuration;
using Stratis.Bitcoin.Features.Api;
using Stratis.Bitcoin.Features.BlockStore;
using Stratis.Bitcoin.Features.ColdStaking;
using Stratis.Bitcoin.Features.Consensus;
using Stratis.Bitcoin.Features.MemoryPool;
using Stratis.Bitcoin.Features.Miner;
using Stratis.Bitcoin.Features.RPC;
using Stratis.Bitcoin.IntegrationTests.Common.EnvironmentMockUpHelpers;
using Stratis.Bitcoin.Networks;
using Stratis.Bitcoin.P2P;
using Stratis.Features.SQLiteWalletRepository;

namespace Stratis.Bitcoin.IntegrationTests.Common.Runners
{
    // TODO: Should the regular PoS runner be changed to use cold staking by default instead of this additional class?
    public sealed class StratisBitcoinColdStakingRunner : NodeRunner
    {
        public StratisBitcoinColdStakingRunner(string dataDir, Network network, string agent = "StratisBitcoin")
            : base(dataDir, agent)
        {
            this.Network = network;
        }

        public override void BuildNode()
        {
            var settings = new NodeSettings(this.Network, ProtocolVersion.PROVEN_HEADER_VERSION, this.Agent, args: new string[] { $"-conf={this.Network.DefaultConfigFilename}", "-datadir=" + this.DataFolder });

            var builder = new FullNodeBuilder()
                .UseNodeSettings(settings)
                .UseBlockStore()
                .UsePosConsensus()
                .UseMempool()
                .UseColdStakingWallet()
                .AddSQLiteWalletRepository()
                .AddPowPosMining(!(this.Network is StratisMain || this.Network is StratisTest || this.Network is StratisRegTest))
                .AddRPC()
                .UseApi()
                .UseTestChainedHeaderTree()
                .MockIBD();

            if (this.OverrideDateTimeProvider)
                builder.OverrideDateTimeProviderFor<MiningFeature>();

            if (!this.EnablePeerDiscovery)
            {
                builder.RemoveImplementation<PeerConnectorDiscovery>();
                builder.ReplaceService<IPeerDiscovery, BaseFeature>(new PeerDiscoveryDisabled());
            }

            this.FullNode = (FullNode)builder.Build();
        }
    }
}
