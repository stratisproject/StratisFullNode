using System;
using System.Linq;
using System.Threading.Tasks;
using NBitcoin.Protocol;
using Stratis.Bitcoin;
using Stratis.Bitcoin.Builder;
using Stratis.Bitcoin.Configuration;
using Stratis.Bitcoin.Consensus;
using Stratis.Bitcoin.Features.Api;
using Stratis.Bitcoin.Features.BlockStore;
using Stratis.Bitcoin.Features.Consensus;
using Stratis.Bitcoin.Features.MemoryPool;
using Stratis.Bitcoin.Features.Miner;
using Stratis.Bitcoin.Features.Notifications;
using Stratis.Bitcoin.Features.RPC;
using Stratis.Bitcoin.Features.SignalR;
using Stratis.Bitcoin.Features.SmartContracts;
using Stratis.Bitcoin.Features.SmartContracts.PoA;
using Stratis.Bitcoin.Features.SmartContracts.Wallet;
using Stratis.Bitcoin.Features.Wallet;
using Stratis.Bitcoin.Networks;
using Stratis.Bitcoin.Utilities;
using Stratis.Features.Collateral;
using Stratis.Features.Collateral.CounterChain;
using Stratis.Features.SQLiteWalletRepository;
using Stratis.Sidechains.Networks;

namespace Stratis.CirrusMinerD
{
    class Program
    {
        private const string MainchainArgument = "-mainchain";
        private const string SidechainArgument = "-sidechain";

        public static void Main(string[] args)
        {
            MainAsync(args).Wait();
        }

        public static async Task MainAsync(string[] args)
        {
            try
            {
                bool isMainchainNode = args.FirstOrDefault(a => a.ToLower() == MainchainArgument) != null;
                bool isSidechainNode = args.FirstOrDefault(a => a.ToLower() == SidechainArgument) != null;
                bool startInDevMode = args.Any(a => a.ToLower().Contains($"-{NodeSettings.DevModeParam}"));

                IFullNode fullNode = null;

                if (startInDevMode)
                {
                    fullNode = BuildDevCirrusMiningNode(args);
                }
                else
                {
                    if (isSidechainNode == isMainchainNode)
                        throw new ArgumentException($"Gateway node needs to be started specifying either a {SidechainArgument} or a {MainchainArgument} argument");

                    fullNode = isMainchainNode ? BuildStraxNode(args) : BuildCirrusMiningNode(args);
                }

                if (fullNode != null)
                {
                    // Set the console window title to identify which node this is (for clarity when running Strax and Cirrus on the same machine).
                    Console.Title = isMainchainNode ? $"Strax Full Node {fullNode.Network.NetworkType}" : $"Cirrus Full Node {fullNode.Network.NetworkType}";

                    await fullNode.RunAsync();
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine("There was a problem initializing the node. Details: '{0}'", ex.Message);
            }
        }

        private static IFullNode BuildCirrusMiningNode(string[] args)
        {
            var nodeSettings = new NodeSettings(networksSelector: CirrusNetwork.NetworksSelector, protocolVersion: ProtocolVersion.CIRRUS_VERSION, args: args)
            {
                MinProtocolVersion = ProtocolVersion.ALT_PROTOCOL_VERSION
            };

            DbType dbType = nodeSettings.GetDbType();

            IFullNode node = new FullNodeBuilder()
                .UseNodeSettings(nodeSettings, dbType)
                .UseBlockStore(dbType)
                .AddPoAFeature()
                .UsePoAConsensus(dbType)
                .AddPoACollateralMiningCapability<SmartContractPoABlockDefinition>()
                .CheckCollateralCommitment()
                .AddDynamicMemberhip()
                .SetCounterChainNetwork(StraxNetwork.MainChainNetworks[nodeSettings.Network.NetworkType]())
                .UseTransactionNotification()
                .UseBlockNotification()
                .UseApi()
                .UseMempool()
                .AddRPC()
                .AddSmartContracts(options =>
                {
                    options.UseReflectionExecutor();
                    options.UsePoAWhitelistedContracts();
                })
                .UseSmartContractWallet()
                .AddSQLiteWalletRepository()
                .Build();

            return node;
        }

        private static IFullNode BuildDevCirrusMiningNode(string[] args)
        {
            string[] devModeArgs = new[] { "-bootstrap=1", "-defaultwalletname=cirrusdev", "-defaultwalletpassword=password" }.Concat(args).ToArray();
            var network = new CirrusDev();

            var nodeSettings = new NodeSettings(network, protocolVersion: ProtocolVersion.CIRRUS_VERSION, args: devModeArgs)
            {
                MinProtocolVersion = ProtocolVersion.ALT_PROTOCOL_VERSION
            };

            DbType dbType = nodeSettings.GetDbType();

            IFullNode node = new FullNodeBuilder()
                .UseNodeSettings(nodeSettings, dbType)
                .UseBlockStore(dbType)
                .AddPoAFeature()
                .UsePoAConsensus(dbType)
                .AddPoAMiningCapability<SmartContractPoABlockDefinition>()
                .UseTransactionNotification()
                .UseBlockNotification()
                .UseApi()
                .UseMempool()
                .AddRPC()
                .AddSmartContracts(options =>
                {
                    options.UseReflectionExecutor();
                    options.UsePoAWhitelistedContracts(true);
                })
                .UseSmartContractWallet()
                .AddSQLiteWalletRepository()
                .AddSignalR(options =>
                {
                    DaemonConfiguration.ConfigureSignalRForCirrus(options);
                })
                .Build();

            return node;
        }

        /// <summary>
        /// Returns a standard Stratis node. Just like StratisD.
        /// </summary>
        /// <param name="args">The command-line arguments.</param>
        /// <returns>See <see cref="IFullNode"/>.</returns>
        private static IFullNode BuildStraxNode(string[] args)
        {
            // TODO: Hardcode -addressindex for better user experience

            var nodeSettings = new NodeSettings(networksSelector: Networks.Strax, protocolVersion: ProtocolVersion.PROVEN_HEADER_VERSION, args: args)
            {
                MinProtocolVersion = ProtocolVersion.ALT_PROTOCOL_VERSION
            };

            DbType dbType = nodeSettings.GetDbType();

            IFullNode node = new FullNodeBuilder()
                .UseNodeSettings(nodeSettings, dbType)
                .UseBlockStore(dbType)
                .UseTransactionNotification()
                .UseBlockNotification()
                .UseApi()
                .UseMempool()
                .AddRPC()
                .UsePosConsensus(dbType)
                .UseWallet()
                .AddSQLiteWalletRepository()
                .AddPowPosMining(true)
                .Build();

            return node;
        }
    }
}
