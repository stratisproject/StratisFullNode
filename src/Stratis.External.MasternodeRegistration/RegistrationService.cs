using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Flurl;
using Flurl.Http;
using NBitcoin;
using Stratis.Bitcoin;
using Stratis.Bitcoin.Controllers.Models;
using Stratis.Bitcoin.Features.BlockStore.Models;
using Stratis.Bitcoin.Networks;
using Stratis.Sidechains.Networks;

namespace Stratis.External.MasternodeRegistration
{
    public sealed class RegistrationService
    {
        private string rootDataFolder;
        private Network mainchainNetwork;
        private Network sidechainNetwork;
        private const string nodeExecutable = "Stratis.CirrusMinerD.exe";

        public async Task StartAsync(NetworkType networkType)
        {
            this.rootDataFolder = Path.Combine(Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location), "StraxMinerD");

            if (networkType == NetworkType.Mainnet)
            {
                this.mainchainNetwork = new StraxMain();
                this.sidechainNetwork = new CirrusMain();
            }
            else
            {
                this.mainchainNetwork = new StraxTest();
                this.sidechainNetwork = new CirrusTest();
            }

            // Start main chain node
            if (!await StartNodeAsync(networkType, NodeType.MainChain))
                return;

            // Wait for main chain node to be initialized
            if (!await EnsureNodeIsInitializedAsync(NodeType.MainChain, this.mainchainNetwork.DefaultAPIPort))
                return;

            // Wait for main chain node to be synced (out of IBD)
            if (!await EnsureNodeIsSyncedAsync(NodeType.MainChain, this.mainchainNetwork.DefaultAPIPort))
                return;

            // Wait for main chain node's address indexer to be synced.
            if (!await EnsureMainChainNodeAddressIndexerIsSyncedAsync())
                return;

            // Start side chain node
            if (!await StartNodeAsync(networkType, NodeType.SideChain))
                return;

            // Wait for side chain node to be initialized
            if (!await EnsureNodeIsInitializedAsync(NodeType.SideChain, this.sidechainNetwork.DefaultAPIPort))
                return;

            // Wait for side chain node to be synced (out of IBD)
            if (!await EnsureNodeIsSyncedAsync(NodeType.SideChain, this.sidechainNetwork.DefaultAPIPort))
                return;

            // Check main chain collateral wallet

            // Check side chain fee wallet        
        }

        private async Task<bool> StartNodeAsync(NetworkType networkType, NodeType nodeType)
        {
            Console.WriteLine($"Starting the {nodeType.ToString()} node on {networkType}...");

            var argumentBuilder = new StringBuilder();

            argumentBuilder.Append($"-{nodeType.ToString().ToLowerInvariant()} ");

            if (nodeType == NodeType.MainChain)
                argumentBuilder.Append("-addressindex=1 ");

            if (nodeType == NodeType.SideChain)
                argumentBuilder.Append($"-counterchainapiport={this.mainchainNetwork.DefaultAPIPort} ");

            if (networkType == NetworkType.Testnet)
                argumentBuilder.Append("-testnet");

            var startInfo = new ProcessStartInfo
            {
                Arguments = argumentBuilder.ToString(),
                FileName = Path.Combine(this.rootDataFolder, nodeExecutable),
                UseShellExecute = true,
            };

            var process = Process.Start(startInfo);
            await Task.Delay(TimeSpan.FromSeconds(5));

            if (process.HasExited)
            {
                Console.WriteLine($"{nodeType.ToString()} node process failed to start, exiting...");
                return false;
            }

            Console.WriteLine($"{nodeType.ToString()} node started.");

            return true;
        }

        private async Task<bool> EnsureNodeIsInitializedAsync(NodeType nodeType, int apiPort)
        {
            Console.WriteLine($"Waiting for the {nodeType.ToString()} node to initialize...");

            bool initialized = false;

            // Call the node status API until the node initialization state is Initialized.
            CancellationToken cancellationTokenSource = new CancellationTokenSource(TimeSpan.FromSeconds(60)).Token;
            do
            {
                if (cancellationTokenSource.IsCancellationRequested)
                {
                    Console.WriteLine($"{nodeType.ToString()} node failed to initialized in 60 seconds...");
                    break;
                }

                StatusModel blockModel = await $"http://localhost:{apiPort}/api".AppendPathSegment("node/status").GetJsonAsync<StatusModel>();
                if (blockModel.State == FullNodeState.Started.ToString())
                {
                    initialized = true;
                    Console.WriteLine($"{nodeType.ToString()} node initialized.");
                    break;
                }

            } while (true);

            return initialized;
        }

        private async Task<bool> EnsureNodeIsSyncedAsync(NodeType nodeType, int apiPort)
        {
            Console.WriteLine($"Waiting for the {nodeType.ToString()} node to sync with the network...");

            bool result;

            // Call the node status API until the node initialization state is Initialized.
            do
            {
                StatusModel blockModel = await $"http://localhost:{apiPort}/api".AppendPathSegment("node/status").GetJsonAsync<StatusModel>();
                if (blockModel.InIbd.HasValue && !blockModel.InIbd.Value)
                {
                    Console.WriteLine($"{nodeType.ToString()} node is synced at height {blockModel.ConsensusHeight}.");
                    result = true;
                    break;
                }

                Console.WriteLine($"{nodeType.ToString()} node syncing, current height {blockModel.ConsensusHeight}...");
                await Task.Delay(TimeSpan.FromSeconds(3));
            } while (true);

            return result;
        }

        private async Task<bool> EnsureMainChainNodeAddressIndexerIsSyncedAsync()
        {
            Console.WriteLine("Waiting for the main chain node to sync it's address indexer...");

            bool result;

            do
            {
                StatusModel blockModel = await $"http://localhost:{this.mainchainNetwork.DefaultAPIPort}/api".AppendPathSegment("node/status").GetJsonAsync<StatusModel>();
                AddressIndexerTipModel addressIndexerModel = await $"http://localhost:{this.mainchainNetwork.DefaultAPIPort}/api".AppendPathSegment("blockstore/addressindexertip").GetJsonAsync<AddressIndexerTipModel>();
                if (addressIndexerModel.TipHeight > (blockModel.ConsensusHeight - 50))
                {
                    Console.WriteLine($"Main chain address indexer synced.");
                    result = true;
                    break;
                }

                Console.WriteLine($"Main chain node address indexer is syncing, current height {addressIndexerModel.TipHeight}...");
                await Task.Delay(TimeSpan.FromSeconds(3));
            } while (true);

            return result;
        }
    }

    public enum NodeType
    {
        MainChain,
        SideChain
    }
}
