using NBitcoin;
using Stratis.Bitcoin;
using Stratis.Bitcoin.Builder;
using Stratis.Bitcoin.Configuration;
using Stratis.Bitcoin.Features.BlockStore;
using Stratis.Bitcoin.Features.Consensus;
using Stratis.Bitcoin.Features.MemoryPool;
using Stratis.Bitcoin.Features.RPC;
using Stratis.Bitcoin.Features.SmartContracts;
using Stratis.Bitcoin.Features.SmartContracts.PoS;
using Stratis.Bitcoin.Features.SmartContracts.Wallet;
using Stratis.Bitcoin.IntegrationTests.Common;
using Stratis.Bitcoin.IntegrationTests.Common.Runners;
using Stratis.Features.SQLiteWalletRepository;

namespace Stratis.SmartContracts.Tests.Common
{
    public sealed class StraxRunner : NodeRunner
    {
        public StraxRunner(string dataDir, Network network)
            : base(dataDir, null)
        {
            this.Network = network;
        }

        public override void BuildNode()
        {
            var settings = new NodeSettings(this.Network, args: new string[] { "-conf=strax.conf", "-datadir=" + this.DataFolder, "-displayextendednodestats=true" });

            IFullNodeBuilder builder = new FullNodeBuilder()
                .UseNodeSettings(settings)
                .UseBlockStore()
                .UseMempool()
                .AddRPC()
                .AddSmartContracts(options =>
                {
                    options.UseReflectionExecutor();
                })
                .UsePosConsensus()
                .UseSmartContractWallet()
                .AddSQLiteWalletRepository()
                .UseSmartContractPosPowMining()
                .MockIBD()
                .UseTestChainedHeaderTree();

            this.FullNode = (FullNode)builder.Build();
        }
    }
}