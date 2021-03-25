using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
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
using Stratis.Bitcoin.Features.Wallet.Models;
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

            Console.Clear();
            Console.WriteLine("SUCCESS: STRAX Blockchain and Cirrus Blockchain are now fully synchronised.");
            Console.WriteLine("Assessing Masternode Requirements...");

            // Check main chain collateral wallet and balace
            if (!await CheckMainChainCollateralAsync(this.mainchainNetwork.DefaultAPIPort))
                return;

            // Check side chain fee wallet        
        }

        private async Task<bool> CheckMainChainCollateralAsync(int apiPort)
        {
            Console.WriteLine("Please Enter the Name of the STRAX Wallet that contains the required collateral of a 100 000 STRAX:");

            var collateralWalletName = Console.ReadLine();

            WalletInfoModel walletInfoModel = await $"http://localhost:{apiPort}/api".AppendPathSegment("Wallet/list-wallets").GetJsonAsync<WalletInfoModel>();

            if (walletInfoModel.WalletNames.Contains(collateralWalletName))
            {
                Console.WriteLine("SUCCESS: Collateral wallet found.");
            }
            else
            {
                Console.WriteLine($"Collateral wallet with name '{collateralWalletName}' does not exist.");

                ConsoleKeyInfo key;
                do
                {
                    Console.WriteLine($"Would you like to restore a wallet that holds the collateral now? Enter (Y) to continue or (N) to exit.");
                    key = Console.ReadKey();
                    if (key.Key == ConsoleKey.Y || key.Key == ConsoleKey.N)
                        break;
                } while (true);

                if (key.Key == ConsoleKey.N)
                {
                    Console.WriteLine($"You have chosen to exit the registration script.");
                    return false;
                }

                if (!await RestoreCollateralWalletAsync(this.mainchainNetwork.DefaultAPIPort, collateralWalletName))
                    return false;
            }

            // Check collateral wallet height (sync) status.
            do
            {
                WalletGeneralInfoModel walletInfo = await $"http://localhost:{this.mainchainNetwork.DefaultAPIPort}/api".AppendPathSegment("wallet/general-info").GetJsonAsync<WalletGeneralInfoModel>();
                StatusModel blockModel = await $"http://localhost:{this.mainchainNetwork.DefaultAPIPort}/api".AppendPathSegment("node/status").GetJsonAsync<StatusModel>();

                if (walletInfo.LastBlockSyncedHeight > (blockModel.ConsensusHeight - 50))
                {
                    Console.WriteLine($"Main chain collateral wallet is synced.");
                    break;
                }

                Console.WriteLine($"Syncing main chain collateral wallet, current height {walletInfo.LastBlockSyncedHeight}...");
                await Task.Delay(TimeSpan.FromSeconds(3));
            } while (true);

            // Check collateral wallet balance.
            try
            {
                var walletBalanceRequest = new WalletBalanceRequest();
                WalletBalanceModel walletBalanceModel = await $"http://localhost:{this.mainchainNetwork.DefaultAPIPort}/api"
                    .AppendPathSegment("wallet/balance")
                    .SetQueryParams(walletBalanceRequest)
                    .GetJsonAsync<WalletBalanceModel>();

                if (walletBalanceModel.AccountsBalances[0].SpendableAmount / 100000000 > 100_000)
                {
                    Console.WriteLine($"SUCCESS: Main chain collateral wallet contains the required collateral amount.");
                    return true;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"ERROR: An exception occurred trying to check the collateral wallet balance: {ex}");
            }

            return false;
        }

        private async Task<bool> RestoreCollateralWalletAsync(int apiPort, string collateralWalletName)
        {
            Console.WriteLine($"You have chosen to restore a mainchain (STRAX) collateral wallet.");

            string collateralMnemonic;
            string collateralPassphrase;
            string collateralPassword;

            do
            {
                Console.WriteLine($"Please enter your 12-Words used to recover your wallet:");
                collateralMnemonic = Console.ReadLine();
                Console.WriteLine("Please enter your wallet passphrase:");
                collateralPassphrase = Console.ReadLine();
                Console.WriteLine("Please enter the wallet password used to encrypt the wallet:");
                collateralPassword = Console.ReadLine();

                if (!string.IsNullOrEmpty(collateralMnemonic) && !string.IsNullOrEmpty(collateralPassphrase) && !string.IsNullOrEmpty(collateralPassword))
                    break;

                Console.WriteLine("ERROR: Please ensure that you enter all the wallet details.");

            } while (true);

            var walletRecoveryRequest = new WalletRecoveryRequest()
            {
                CreationDate = new DateTime(2020, 11, 1),
                Mnemonic = collateralMnemonic,
                Name = collateralWalletName,
                Passphrase = collateralPassphrase,
                Password = collateralPassword
            };

            try
            {
                await $"http://localhost:{apiPort}/api".AppendPathSegment("wallet/recover").PostJsonAsync(walletRecoveryRequest);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"ERROR: An exception occurred trying to recover your collateral wallet {ex}");
                return false;
            }

            WalletInfoModel walletInfoModel = await $"http://localhost:{apiPort}/api".AppendPathSegment("Wallet/list-wallets").GetJsonAsync<WalletInfoModel>();
            if (walletInfoModel.WalletNames.Contains(collateralWalletName))
            {
                Console.WriteLine("SUCCESS: Collateral wallet has been restored.");
            }
            else
            {
                Console.WriteLine("ERROR: Collateral wallet failed to be restored, exiting the regisrtation process.");
                return false;
            }

            try
            {
                Console.WriteLine("The collateral wallet will now be resynced, please be patient...");
                var walletSyncRequest = new WalletSyncRequest()
                {
                    All = true,
                    WalletName = collateralWalletName
                };

                await $"http://localhost:{apiPort}/api".AppendPathSegment("wallet/sync-from-date").PostJsonAsync(walletSyncRequest);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"ERROR: An exception occurred trying to resync your collateral wallet {ex}");
                return false;
            }

            return true;
        }

        private async Task<bool> StartNodeAsync(NetworkType networkType, NodeType nodeType)
        {
            Console.WriteLine($"Starting the {nodeType} node on {networkType}...");

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
                Console.WriteLine($"{nodeType} node process failed to start, exiting...");
                return false;
            }

            Console.WriteLine($"{nodeType} node started.");

            return true;
        }

        private async Task<bool> EnsureNodeIsInitializedAsync(NodeType nodeType, int apiPort)
        {
            Console.WriteLine($"Waiting for the {nodeType} node to initialize...");

            bool initialized = false;

            // Call the node status API until the node initialization state is Initialized.
            CancellationToken cancellationTokenSource = new CancellationTokenSource(TimeSpan.FromSeconds(60)).Token;
            do
            {
                if (cancellationTokenSource.IsCancellationRequested)
                {
                    Console.WriteLine($"{nodeType} node failed to initialized in 60 seconds...");
                    break;
                }

                StatusModel blockModel = await $"http://localhost:{apiPort}/api".AppendPathSegment("node/status").GetJsonAsync<StatusModel>();
                if (blockModel.State == FullNodeState.Started.ToString())
                {
                    initialized = true;
                    Console.WriteLine($"{nodeType} node initialized.");
                    break;
                }

            } while (true);

            return initialized;
        }

        private async Task<bool> EnsureNodeIsSyncedAsync(NodeType nodeType, int apiPort)
        {
            Console.WriteLine($"Waiting for the {nodeType} node to sync with the network...");

            bool result;

            // Call the node status API until the node initialization state is Initialized.
            do
            {
                StatusModel blockModel = await $"http://localhost:{apiPort}/api".AppendPathSegment("node/status").GetJsonAsync<StatusModel>();
                if (blockModel.InIbd.HasValue && !blockModel.InIbd.Value)
                {
                    Console.WriteLine($"{nodeType} node is synced at height {blockModel.ConsensusHeight}.");
                    result = true;
                    break;
                }

                Console.WriteLine($"{nodeType} node syncing, current height {blockModel.ConsensusHeight}...");
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
