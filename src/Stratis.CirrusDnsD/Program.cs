using System;
using System.Threading.Tasks;
using NBitcoin.Protocol;
using Stratis.Bitcoin;
using Stratis.Bitcoin.Builder;
using Stratis.Bitcoin.Configuration;
using Stratis.Bitcoin.Consensus;
using Stratis.Bitcoin.Features.Api;
using Stratis.Bitcoin.Features.BlockStore;
using Stratis.Bitcoin.Features.Dns;
using Stratis.Bitcoin.Features.MemoryPool;
using Stratis.Bitcoin.Features.RPC;
using Stratis.Bitcoin.Features.SmartContracts;
using Stratis.Bitcoin.Features.SmartContracts.PoA;
using Stratis.Bitcoin.Features.SmartContracts.Wallet;
using Stratis.Bitcoin.Networks;
using Stratis.Bitcoin.Utilities;
using Stratis.Features.Collateral;
using Stratis.Features.Collateral.CounterChain;
using Stratis.Features.SQLiteWalletRepository;
using Stratis.Sidechains.Networks;

namespace Stratis.CirrusDnsD
{
    /// <summary>
    /// Main entry point.
    /// </summary>
    public class Program
    {
        /// <summary>
        /// The async entry point for the Cirrus Dns process.
        /// </summary>
        /// <param name="args">Command line arguments.</param>
        /// <returns>A task used to await the operation.</returns>
        public static async Task Main(string[] args)
        {
            try
            {
                var nodeSettings = new NodeSettings(networksSelector: CirrusNetwork.NetworksSelector, protocolVersion: ProtocolVersion.CIRRUS_VERSION, args: args)
                {
                    MinProtocolVersion = ProtocolVersion.ALT_PROTOCOL_VERSION
                };

                var dnsSettings = new DnsSettings(nodeSettings);

                if (string.IsNullOrWhiteSpace(dnsSettings.DnsHostName) || string.IsNullOrWhiteSpace(dnsSettings.DnsNameServer) || string.IsNullOrWhiteSpace(dnsSettings.DnsMailBox))
                    throw new ConfigurationException("When running as a DNS Seed service, the -dnshostname, -dnsnameserver and -dnsmailbox arguments must be specified on the command line.");

                IFullNode node;
                if (dnsSettings.DnsFullNode)
                {
                    // Build the Dns full node.
                    node = GetSideChainFullNode(nodeSettings);
                }
                else
                {
                    // Build the Dns node.
                    node = GetDnsNode(nodeSettings);
                }

                // Run node.
                if (node != null)
                    await node.RunAsync();
            }
            catch (Exception ex)
            {
                Console.WriteLine("There was a problem initializing the node. Details: '{0}'", ex.ToString());
            }
        }

        private static IFullNode GetSideChainFullNode(NodeSettings nodeSettings)
        {
            DbType dbType = nodeSettings.GetDbType();

            IFullNodeBuilder nodeBuilder = new FullNodeBuilder()
            .UseNodeSettings(nodeSettings, dbType)
            .UseBlockStore(dbType)
            .UseMempool()
            .AddSmartContracts(options =>
            {
                options.UseReflectionExecutor();
                options.UsePoAWhitelistedContracts();
            })
            .AddPoAFeature()
            .UsePoAConsensus(dbType)
            .CheckCollateralCommitment()
            .AddDynamicMemberhip()

            // This needs to be set so that we can check the magic bytes during the Strat to Strax changeover.
            // Perhaps we can introduce a block height check rather?
            .SetCounterChainNetwork(StraxNetwork.MainChainNetworks[nodeSettings.Network.NetworkType]())

            .UseSmartContractWallet()
            .AddSQLiteWalletRepository()
            .UseApi()
            .AddRPC()
            .UseDns();

            return nodeBuilder.Build();
        }

        private static IFullNode GetDnsNode(NodeSettings nodeSettings)
        {
            IFullNode node = new FullNodeBuilder()
                .UseNodeSettings(nodeSettings)
                .AddPoAFeature()
                .UsePoAConsensus()
                .UseApi()
                .AddRPC()
                .UseDns()
                .AddRPC()
                .Build();

            return node;
        }
    }
}