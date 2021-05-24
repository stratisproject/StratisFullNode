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
using NBitcoin.DataEncoders;
using Stratis.Bitcoin;
using Stratis.Bitcoin.Controllers.Models;
using Stratis.Bitcoin.Features.BlockStore.Models;
using Stratis.Bitcoin.Features.PoA;
using Stratis.Bitcoin.Features.PoA.Models;
using Stratis.Bitcoin.Features.Wallet.Models;
using Stratis.Bitcoin.Networks;
using Stratis.Features.PoA.Voting;
using Stratis.Sidechains.Networks;

namespace Stratis.External.Masternodes
{
    public sealed class RegistrationService
    {
        /// <summary>The folder where the CirrusMinerD.exe is stored.</summary>
        private string nodeExecutablesPath;
        private Network mainchainNetwork;
        private Network sidechainNetwork;
        private const string nodeExecutable = "Stratis.CirrusMinerD.exe";

        private const int CollateralRequirement = 100_000;
        private const int FeeRequirement = 500;

        private string rootDataDir;

        public async Task StartAsync(NetworkType networkType)
        {
            this.rootDataDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "StratisNode");
            this.nodeExecutablesPath = Path.Combine(Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location), "StraxMinerD");

            if (networkType == NetworkType.Mainnet)
            {
                this.mainchainNetwork = new StraxMain();
                this.sidechainNetwork = new CirrusMain();
            }

            if (networkType == NetworkType.Testnet)
            {
                this.mainchainNetwork = new StraxTest();
                this.sidechainNetwork = new CirrusTest();
            }

            if (networkType == NetworkType.Regtest)
            {
                this.mainchainNetwork = new StraxRegTest();
                this.sidechainNetwork = new CirrusRegTest();
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

            // Create the masternode public key.
            if (!CreateFederationKey())
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
            if (!await CheckWalletRequirementsAsync(NodeType.MainChain, this.mainchainNetwork.DefaultAPIPort))
                return;

            // Check side chain fee wallet
            if (!await CheckWalletRequirementsAsync(NodeType.SideChain, this.sidechainNetwork.DefaultAPIPort))
                return;

            // Call the join federation API call.
            if (!await CallJoinFederationRequestAsync())
                return;

            // Call the join federation API call.
            await MonitorJoinFederationRequestAsync();
        }

        private async Task<bool> StartNodeAsync(NetworkType networkType, NodeType nodeType)
        {
            var argumentBuilder = new StringBuilder();

            argumentBuilder.Append($"-{nodeType.ToString().ToLowerInvariant()} ");

            if (nodeType == NodeType.MainChain)
                argumentBuilder.Append("-addressindex=1 ");

            if (nodeType == NodeType.SideChain)
                argumentBuilder.Append($"-counterchainapiport={this.mainchainNetwork.DefaultAPIPort} ");

            if (networkType == NetworkType.Testnet)
                argumentBuilder.Append("-testnet");

            if (networkType == NetworkType.Regtest)
                argumentBuilder.Append("-regtest");

            Console.WriteLine($"Starting the {nodeType} node on {networkType}.");
            Console.WriteLine($"Start up arguments: {argumentBuilder}");

            var startInfo = new ProcessStartInfo
            {
                Arguments = argumentBuilder.ToString(),
                FileName = Path.Combine(this.nodeExecutablesPath, nodeExecutable),
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

        private async Task<bool> CheckWalletRequirementsAsync(NodeType nodeType, int apiPort)
        {
            var chainName = nodeType == NodeType.MainChain ? "STRAX" : "CIRRUS";
            var amountToCheck = nodeType == NodeType.MainChain ? CollateralRequirement : FeeRequirement;
            var chainTicker = nodeType == NodeType.MainChain ? this.mainchainNetwork.CoinTicker : this.sidechainNetwork.CoinTicker;

            Console.WriteLine($"Please enter the name of the {chainName} wallet that contains the required collateral of {amountToCheck} {chainTicker}:");

            var walletName = Console.ReadLine();

            WalletInfoModel walletInfoModel = await $"http://localhost:{apiPort}/api".AppendPathSegment("Wallet/list-wallets").GetJsonAsync<WalletInfoModel>();

            if (walletInfoModel.WalletNames.Contains(walletName))
            {
                Console.WriteLine($"SUCCESS: Wallet with name '{chainName}' found.");
            }
            else
            {
                Console.WriteLine($"{chainName} wallet with name '{walletName}' does not exist.");

                ConsoleKeyInfo key;
                do
                {
                    Console.WriteLine($"Would you like to restore you {chainName} wallet that holds the required amount of {amountToCheck} {chainTicker} now? Enter (Y) to continue or (N) to exit.");
                    key = Console.ReadKey();
                    if (key.Key == ConsoleKey.Y || key.Key == ConsoleKey.N)
                        break;
                } while (true);

                if (key.Key == ConsoleKey.N)
                {
                    Console.WriteLine($"You have chosen to exit the registration script.");
                    return false;
                }

                if (!await RestoreWalletAsync(apiPort, chainName, walletName))
                    return false;
            }

            // Check wallet height (sync) status.
            do
            {
                var walletNameRequest = new WalletName() { Name = walletName };
                WalletGeneralInfoModel walletInfo = await $"http://localhost:{apiPort}/api".AppendPathSegment("wallet/general-info").SetQueryParams(walletNameRequest).GetJsonAsync<WalletGeneralInfoModel>();
                StatusModel blockModel = await $"http://localhost:{apiPort}/api".AppendPathSegment("node/status").GetJsonAsync<StatusModel>();

                if (walletInfo.LastBlockSyncedHeight > (blockModel.ConsensusHeight - 50))
                {
                    Console.WriteLine($"{chainName} wallet is synced.");
                    break;
                }

                Console.WriteLine($"Syncing {chainName} wallet, current height {walletInfo.LastBlockSyncedHeight}...");
                await Task.Delay(TimeSpan.FromSeconds(3));
            } while (true);

            // Check wallet balance.
            try
            {
                var walletBalanceRequest = new WalletBalanceRequest() { WalletName = walletName };
                WalletBalanceModel walletBalanceModel = await $"http://localhost:{apiPort}/api"
                    .AppendPathSegment("wallet/balance")
                    .SetQueryParams(walletBalanceRequest)
                    .GetJsonAsync<WalletBalanceModel>();

                if (walletBalanceModel.AccountsBalances[0].SpendableAmount / 100000000 > amountToCheck)
                {
                    Console.WriteLine($"SUCCESS: The {chainName} wallet contains the required amount of {amountToCheck} {chainTicker}.");
                    return true;
                }

                Console.WriteLine($"ERROR: The {chainName} wallet does not contain the required amount of {amountToCheck} {chainTicker}.");

            }
            catch (Exception ex)
            {
                Console.WriteLine($"ERROR: An exception occurred trying to check the wallet balance: {ex}");
            }

            return false;
        }

        private async Task<bool> RestoreWalletAsync(int apiPort, string chainName, string walletName)
        {
            Console.WriteLine($"You have chosen to restore your {chainName} wallet.");

            string mnemonic;
            string passphrase;
            string password;

            do
            {
                Console.WriteLine($"Please enter your 12-Words used to recover your wallet:");
                mnemonic = Console.ReadLine();
                Console.WriteLine("Please enter your wallet passphrase:");
                passphrase = Console.ReadLine();
                Console.WriteLine("Please enter the wallet password used to encrypt the wallet:");
                password = Console.ReadLine();

                if (!string.IsNullOrEmpty(mnemonic) && !string.IsNullOrEmpty(passphrase) && !string.IsNullOrEmpty(password))
                    break;

                Console.WriteLine("ERROR: Please ensure that you enter all the wallet details.");

            } while (true);

            var walletRecoveryRequest = new WalletRecoveryRequest()
            {
                CreationDate = new DateTime(2020, 11, 1),
                Mnemonic = mnemonic,
                Name = walletName,
                Passphrase = passphrase,
                Password = password
            };

            try
            {
                await $"http://localhost:{apiPort}/api".AppendPathSegment("wallet/recover").PostJsonAsync(walletRecoveryRequest);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"ERROR: An exception occurred trying to recover your {chainName} wallet: {ex}");
                return false;
            }

            WalletInfoModel walletInfoModel = await $"http://localhost:{apiPort}/api".AppendPathSegment("Wallet/list-wallets").GetJsonAsync<WalletInfoModel>();
            if (walletInfoModel.WalletNames.Contains(walletName))
            {
                Console.WriteLine($"SUCCESS: {chainName} wallet has been restored.");
            }
            else
            {
                Console.WriteLine($"ERROR: {chainName} wallet failed to be restored, exiting the registration process.");
                return false;
            }

            try
            {
                Console.WriteLine($"Your {chainName} wallet will now be resynced, please be patient...");
                var walletSyncRequest = new WalletSyncRequest()
                {
                    All = true,
                    WalletName = walletName
                };

                await $"http://localhost:{apiPort}/api".AppendPathSegment("wallet/sync-from-date").PostJsonAsync(walletSyncRequest);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"ERROR: An exception occurred trying to resync your {chainName} wallet: {ex}");
                return false;
            }

            return true;
        }

        private bool CreateFederationKey()
        {
            var keyFilePath = Path.Combine(this.rootDataDir, this.sidechainNetwork.RootFolderName, this.sidechainNetwork.Name, KeyTool.KeyFileDefaultName);

            if (File.Exists(keyFilePath))
            {
                Console.WriteLine($"Your masternode public key file already exists.");
                return true;
            }

            Console.Clear();
            Console.WriteLine($"Your masternode public key will now be generated.");

            string publicKeyPassphrase;

            do
            {
                Console.WriteLine($"Please enter a passphrase (this can be anything, but please write it down):");
                publicKeyPassphrase = Console.ReadLine();

                if (!string.IsNullOrEmpty(publicKeyPassphrase))
                    break;

                Console.WriteLine("ERROR: Please ensure that you enter a valid passphrase.");

            } while (true);



            // Generate keys for mining.
            var tool = new KeyTool(keyFilePath);

            Key key = tool.GeneratePrivateKey();

            string savePath = tool.GetPrivateKeySavePath();
            tool.SavePrivateKey(key);
            PubKey miningPubKey = key.PubKey;

            Console.WriteLine($"Your Masternode Public Key (PubKey) is: {Encoders.Hex.EncodeData(miningPubKey.ToBytes(false))}");

            if (publicKeyPassphrase != null)
            {
                Console.WriteLine(Environment.NewLine);
                Console.WriteLine($"Your passphrase: {publicKeyPassphrase}");
            }

            Console.WriteLine(Environment.NewLine);
            Console.WriteLine($"It has been saved in the root Cirrus data folder: {savePath}");
            Console.WriteLine($"Please ensure that you take a backup of this file.");
            return true;
        }

        private async Task<bool> CallJoinFederationRequestAsync()
        {
            Console.Clear();
            Console.WriteLine($"The relevant masternode registration wallets has now been setup and verified.");
            Console.WriteLine($"Press any key to continue (this will deduct the registation fee from your Cirrus wallet)");
            Console.ReadKey();

            string collateralWallet;
            string collateralPassword;
            string collateralAddress;
            string cirrusWalletName;
            string cirrusWalletPassword;

            do
            {
                Console.WriteLine($"[Strax] Please enter the collateral wallet name:");
                collateralWallet = Console.ReadLine();
                Console.WriteLine($"[Strax] Please enter the collateral wallet password:");
                collateralPassword = Console.ReadLine();
                Console.WriteLine($"[Strax] Please enter the collateral address in which the collateral amount of {CollateralRequirement} {this.mainchainNetwork.CoinTicker} is held:");
                collateralAddress = Console.ReadLine();

                Console.WriteLine($"[Cirrus] Please enter the wallet name which holds the registration fee of {FeeRequirement} {this.sidechainNetwork.CoinTicker}:");
                cirrusWalletName = Console.ReadLine();
                Console.WriteLine($"[Cirrus] Please enter the above wallet's password:");
                cirrusWalletPassword = Console.ReadLine();

                if (!string.IsNullOrEmpty(collateralWallet) && !string.IsNullOrEmpty(collateralPassword) && !string.IsNullOrEmpty(collateralAddress) &&
                    !string.IsNullOrEmpty(cirrusWalletName) && !string.IsNullOrEmpty(cirrusWalletPassword))
                    break;

                Console.WriteLine("ERROR: Please ensure that you enter the relevant details correctly.");

            } while (true);

            var request = new JoinFederationRequestModel()
            {
                CollateralAddress = collateralAddress,
                CollateralWalletName = collateralWallet,
                CollateralWalletPassword = collateralPassword,
                WalletAccount = "account 0",
                WalletName = cirrusWalletName,
                WalletPassword = cirrusWalletPassword
            };

            try
            {
                await $"http://localhost:{this.sidechainNetwork.DefaultAPIPort}/api".AppendPathSegment("collateral/joinfederation").PostJsonAsync(request);
                Console.WriteLine($"SUCCESS: The masternode request has now been submitted to the network,please press any key to view its progress.");
                return true;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"ERROR: An exception occurred trying to registre your masternode: {ex}");
                return false;
            }
        }

        private async Task MonitorJoinFederationRequestAsync()
        {
            // Check wallet height (sync) status.
            do
            {
                FederationMemberDetailedModel memberInfo = await $"http://localhost:{this.sidechainNetwork.DefaultAPIPort}/api".AppendPathSegment("federation/members/current").GetJsonAsync<FederationMemberDetailedModel>();
                StatusModel blockModel = await $"http://localhost:{this.sidechainNetwork.DefaultAPIPort}/api".AppendPathSegment("node/status").GetJsonAsync<StatusModel>();

                Console.Clear();
                Console.WriteLine($">> Registration Progress");
                Console.WriteLine($"PubKey".PadRight(30) + $": {memberInfo.PubKey}");
                Console.WriteLine($"Current Height".PadRight(30) + $": {blockModel.ConsensusHeight}");
                Console.WriteLine($"Mining will start at height".PadRight(30) + $": {memberInfo.MemberWillStartMiningAtBlockHeight}");
                Console.WriteLine($"Rewards will start at height".PadRight(30) + $": {memberInfo.MemberWillStartEarningRewardsEstimateHeight}");
                Console.WriteLine();
                Console.WriteLine($"Press CRTL-C to exit...");
                await Task.Delay(TimeSpan.FromSeconds(5));
            } while (true);
        }
    }

    public enum NodeType
    {
        MainChain,
        SideChain
    }
}
