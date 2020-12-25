﻿using System;
using System.Threading.Tasks;
using NBitcoin.Protocol;
using Stratis.Bitcoin;
using Stratis.Bitcoin.Builder;
using Stratis.Bitcoin.Configuration;
using Stratis.Bitcoin.Features.Api;
using Stratis.Bitcoin.Features.BlockStore;
using Stratis.Bitcoin.Features.MemoryPool;
using Stratis.Bitcoin.Features.RPC;
using Stratis.Bitcoin.Features.SignalR;
using Stratis.Bitcoin.Features.SignalR.Broadcasters;
using Stratis.Bitcoin.Features.SignalR.Events;
using Stratis.Bitcoin.Features.SmartContracts;
using Stratis.Bitcoin.Features.SmartContracts.PoA;
using Stratis.Bitcoin.Features.SmartContracts.Wallet;
using Stratis.Bitcoin.Networks;
using Stratis.Bitcoin.Utilities;
using Stratis.Features.Collateral;
using Stratis.Features.Collateral.CounterChain;
using Stratis.Features.Diagnostic;
using Stratis.Features.SQLiteWalletRepository;
using Stratis.Sidechains.Networks;

namespace Stratis.CirrusD
{
    class Program
    {
        public static void Main(string[] args)
        {
            MainAsync(args).Wait();
        }

        public static async Task MainAsync(string[] args)
        {
            try
            {
                var nodeSettings = new NodeSettings(networksSelector: CirrusNetwork.NetworksSelector, protocolVersion: ProtocolVersion.CIRRUS_VERSION, args: args)
                {
                    MinProtocolVersion = ProtocolVersion.ALT_PROTOCOL_VERSION
                };

                IFullNode node = GetSideChainFullNode(nodeSettings);

                if (node != null)
                    await node.RunAsync();
            }
            catch (Exception ex)
            {
                Console.WriteLine("There was a problem initializing the node. Details: '{0}'", ex.Message);
            }
        }

        private static IFullNode GetSideChainFullNode(NodeSettings nodeSettings)
        {
            IFullNodeBuilder nodeBuilder = new FullNodeBuilder()
                .UseNodeSettings(nodeSettings)
                .UseBlockStore()
                .UseMempool()
                .AddSmartContracts(options =>
                {
                    options.UseReflectionExecutor();
                    options.UsePoAWhitelistedContracts();
                })
                .AddPoAFeature()
                .UsePoAConsensus()
                .CheckCollateralCommitment()

                // This needs to be set so that we can check the magic bytes during the Strat to Strax changeover.
                // Perhaps we can introduce a block height check rather?
                .SetCounterChainNetwork(StraxNetwork.MainChainNetworks[nodeSettings.Network.NetworkType]())

                .UseSmartContractWallet()
                .AddSQLiteWalletRepository()
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
                        (Broadcaster: typeof(CirrusWalletInfoBroadcaster),
                            ClientEventBroadcasterSettings: new ClientEventBroadcasterSettings
                            {
                                BroadcastFrequencySeconds = 5
                            })
                    };
                });
            }

            return nodeBuilder.Build();
        }
    }
}