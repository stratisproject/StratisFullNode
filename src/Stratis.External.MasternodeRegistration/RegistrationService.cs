using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Flurl;
using Flurl.Http;
using NBitcoin;
using Stratis.Bitcoin;
using Stratis.Bitcoin.Controllers.Models;
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
            if (!await StartMainChainNodeAsync(networkType))
                return;

            // Wait for main chain node to be initialized
            if (!await EnsureMainChainNodeIsInitializedAsync())
                return;

            // Wait for main chain node to be synced (out of IBD)
            if (!await EnsureMainChainNodeIsSyncedAsync())
                return;

            // Start side chain node

            // Wait for side chain node to be initialized

            // Wait for side chain node to be synced (out of IBD)

            // Check main chain collateral wallet

            // Check side chain fee wallet        
        }

        private async Task<bool> StartMainChainNodeAsync(NetworkType networkType)
        {
            Console.WriteLine("Starting the main chain node...");

            var startInfo = new ProcessStartInfo
            {
                Arguments = $"-mainchain",
                FileName = Path.Combine(this.rootDataFolder, nodeExecutable),
                UseShellExecute = true,

            };

            var process = Process.Start(startInfo);
            await Task.Delay(TimeSpan.FromSeconds(5));

            if (process.HasExited)
            {
                Console.WriteLine("Main chain node process failed to start, exiting...");
                return false;
            }

            Console.WriteLine("Main chain node started...");

            return true;
        }

        private async Task<bool> EnsureMainChainNodeIsInitializedAsync()
        {
            Console.WriteLine("Waiting for the main chain node to initialize...");

            bool initialized = false;

            // Call the node status API until the node initialization state is Initialized.

            CancellationToken cancellationTokenSource = new CancellationTokenSource(TimeSpan.FromSeconds(60)).Token;
            do
            {
                if (cancellationTokenSource.IsCancellationRequested)
                {
                    Console.WriteLine("Main chain node failed to initialized in 60 seconds...");
                    break;
                }

                StatusModel blockModel = await $"http://localhost:{this.mainchainNetwork.DefaultAPIPort}/api".AppendPathSegment("node/status").GetJsonAsync<StatusModel>();
                if (blockModel.State == FullNodeState.Started.ToString())
                {
                    initialized = true;
                    Console.WriteLine("Main chain node initialized...");
                    break;
                }

            } while (true);

            return initialized;
        }

        private async Task<bool> EnsureMainChainNodeIsSyncedAsync()
        {
            Console.WriteLine("Waiting for the main chain node to sync with the network...");

            bool result;

            // Call the node status API until the node initialization state is Initialized.
            do
            {
                StatusModel blockModel = await $"http://localhost:{this.mainchainNetwork.DefaultAPIPort}/api".AppendPathSegment("node/status").GetJsonAsync<StatusModel>();
                if (blockModel.InIbd.HasValue && !blockModel.InIbd.Value)
                {
                    Console.WriteLine($"Main chain node is synced at height {blockModel.ConsensusHeight}");
                    result = true;
                    break;
                }

                Console.WriteLine($"Main chain node syncing, current height {blockModel.ConsensusHeight}...");
                await Task.Delay(TimeSpan.FromSeconds(3));
            } while (true);

            return result;
        }
    }
}
