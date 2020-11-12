using System;
using System.IO;
using System.Threading.Tasks;
using NBitcoin;
using NBitcoin.Protocol;
using Stratis.Bitcoin;
using Stratis.Bitcoin.Builder;
using Stratis.Bitcoin.Configuration;
using Stratis.Bitcoin.Configuration.Logging;
using Stratis.Bitcoin.Features.Api;
using Stratis.Bitcoin.Features.BlockStore;
using Stratis.Bitcoin.Features.ColdStaking;
using Stratis.Bitcoin.Features.Consensus;
using Stratis.Bitcoin.Features.Consensus.ProvenBlockHeaders;
using Stratis.Bitcoin.Features.MemoryPool;
using Stratis.Bitcoin.Features.Miner;
using Stratis.Bitcoin.Features.RPC;
using Stratis.Bitcoin.Features.SignalR;
using Stratis.Bitcoin.Features.SignalR.Broadcasters;
using Stratis.Bitcoin.Features.SignalR.Events;
using Stratis.Bitcoin.Networks;
using Stratis.Bitcoin.Utilities;
using Stratis.Features.Diagnostic;
using Stratis.Features.SQLiteWalletRepository;

namespace Stratis.StraxD
{
    public class Program
    {
        public static async Task Main(string[] args)
        {
            try
            {
                var nodeSettings = new NodeSettings(networksSelector: Networks.Strax, protocolVersion: ProtocolVersion.PROVEN_HEADER_VERSION, args: args)
                {
                    MinProtocolVersion = ProtocolVersion.PROVEN_HEADER_VERSION
                };

                await CheckLegacyGenesisHashAsync(nodeSettings);

                IFullNodeBuilder nodeBuilder = new FullNodeBuilder()
                    .UseNodeSettings(nodeSettings)
                    .UseBlockStore()
                    .UsePosConsensus()
                    .UseMempool()
                    .UseColdStakingWallet()
                    .AddSQLiteWalletRepository()
                    .AddPowPosMining(true)
                    .UseApi()
                    .AddRPC()
                    .UseDiagnosticFeature();

                if (nodeSettings.EnableSignalR)
                {
                    nodeBuilder.AddSignalR(options =>
                    {
                        options.EventsToHandle = new[]
                        {
                            (IClientEvent) new BlockConnectedClientEvent(),
                            new TransactionReceivedClientEvent()
                        };

                        options.ClientEventBroadcasters = new[]
                        {
                            (Broadcaster: typeof(StakingBroadcaster), ClientEventBroadcasterSettings: new ClientEventBroadcasterSettings
                                {
                                    BroadcastFrequencySeconds = 5
                                }),
                            (Broadcaster: typeof(WalletInfoBroadcaster), ClientEventBroadcasterSettings: new ClientEventBroadcasterSettings
                                {
                                    BroadcastFrequencySeconds = 5
                                })
                        };
                    });
                }

                IFullNode node = nodeBuilder.Build();

                if (node != null)
                    await node.RunAsync();
            }
            catch (Exception ex)
            {
                Console.WriteLine("There was a problem initializing the node. Details: '{0}'", ex);
            }
        }

        private static async Task CheckLegacyGenesisHashAsync(NodeSettings nodeSettings)
        {
            var store = new ProvenBlockHeaderRepository(nodeSettings.Network, nodeSettings.DataFolder, ExtendedLoggerFactory.Create(), new DBreezeSerializer(nodeSettings.Network.Consensus.ConsensusFactory));
            await store.InitializeAsync();
            store.Dispose();

            if (store.TipHashHeight.Hash == new uint256("00000921702bd55eb8c4318a8dbcfca29b9d340b1856c6af0b8962a3a0e12fff") && store.TipHashHeight.Height == 0)
            {
                if (Directory.Exists(Path.Combine(nodeSettings.DataDir, "blocks")))
                    DeleteFolderContents(Path.Combine(nodeSettings.DataDir, "blocks"));

                if (Directory.Exists(Path.Combine(nodeSettings.DataDir, "chain")))
                    DeleteFolderContents(Path.Combine(nodeSettings.DataDir, "chain"));

                if (Directory.Exists(Path.Combine(nodeSettings.DataDir, "coindb")))
                    DeleteFolderContents(Path.Combine(nodeSettings.DataDir, "coindb"));

                if (Directory.Exists(Path.Combine(nodeSettings.DataDir, "common")))
                    DeleteFolderContents(Path.Combine(nodeSettings.DataDir, "common"));

                if (Directory.Exists(Path.Combine(nodeSettings.DataDir, "provenheaders")))
                    DeleteFolderContents(Path.Combine(nodeSettings.DataDir, "provenheaders"));

                File.Delete(Path.Combine(nodeSettings.DataDir, "peers.json"));
            }
        }

        private static void DeleteFolderContents(string folder)
        {
            foreach (string fileName in Directory.EnumerateFiles(folder))
            {
                File.Delete(fileName);
            }
        }
    }
}
