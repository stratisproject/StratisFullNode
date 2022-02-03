using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using NBitcoin;
using NBitcoin.Policy;
using Stratis.Bitcoin.Builder;
using Stratis.Bitcoin.Builder.Feature;
using Stratis.Bitcoin.Configuration.Logging;
using Stratis.Bitcoin.Connection;
using Stratis.Bitcoin.Consensus;
using Stratis.Bitcoin.Features.BlockStore;
using Stratis.Bitcoin.Features.MemoryPool;
using Stratis.Bitcoin.Features.RPC;
using Stratis.Bitcoin.Features.Wallet.Broadcasting;
using Stratis.Bitcoin.Features.Wallet.Interfaces;
using Stratis.Bitcoin.Features.Wallet.Services;
using Stratis.Bitcoin.Interfaces;
using Stratis.Bitcoin.Utilities;

namespace Stratis.Bitcoin.Features.Wallet
{
    /// <summary>
    /// Common base class for any feature replacing the <see cref="WalletFeature" />.
    /// </summary>
    public abstract class BaseWalletFeature : FullNodeFeature
    {
    }

    /// <summary>
    /// Wallet feature for the full node.
    /// </summary>
    /// <seealso cref="FullNodeFeature" />
    public class WalletFeature : BaseWalletFeature
    {
        private readonly IWalletSyncManager walletSyncManager;

        private readonly IWalletManager walletManager;

        private readonly IConnectionManager connectionManager;

        private readonly IAddressBookManager addressBookManager;

        private readonly BroadcasterBehavior broadcasterBehavior;

        private readonly IWalletRepository walletRepository;

        /// <summary>
        /// Initializes a new instance of the <see cref="WalletFeature"/> class.
        /// </summary>
        /// <param name="walletSyncManager">The synchronization manager for the wallet, tasked with keeping the wallet synced with the network.</param>
        /// <param name="walletManager">The wallet manager.</param>
        /// <param name="addressBookManager">The address book manager.</param>
        /// <param name="connectionManager">The connection manager.</param>
        /// <param name="broadcasterBehavior">The broadcaster behavior.</param>
        /// <param name="nodeStats">See <see cref="INodeStats"/>.</param>
        /// <param name="walletRepository">See <see cref="IWalletRepository"/>.</param>
        public WalletFeature(
            IWalletSyncManager walletSyncManager,
            IWalletManager walletManager,
            IAddressBookManager addressBookManager,
            IConnectionManager connectionManager,
            BroadcasterBehavior broadcasterBehavior,
            INodeStats nodeStats,
            IWalletRepository walletRepository)
        {
            this.walletSyncManager = walletSyncManager;
            this.walletManager = walletManager;
            this.addressBookManager = addressBookManager;
            this.connectionManager = connectionManager;
            this.broadcasterBehavior = broadcasterBehavior;
            this.walletRepository = walletRepository;

            nodeStats.RegisterStats(this.AddComponentStats, StatsType.Component, this.GetType().Name);
            nodeStats.RegisterStats(this.AddInlineStats, StatsType.Inline, this.GetType().Name, 800);
        }

        /// <summary>
        /// Prints command-line help.
        /// </summary>
        /// <param name="network">The network to extract values from.</param>
        public static void PrintHelp(Network network)
        {
            WalletSettings.PrintHelp(network);
        }

        /// <summary>
        /// Get the default configuration.
        /// </summary>
        /// <param name="builder">The string builder to add the settings to.</param>
        /// <param name="network">The network to base the defaults off.</param>
        public static void BuildDefaultConfigurationFile(StringBuilder builder, Network network)
        {
            WalletSettings.BuildDefaultConfigurationFile(builder, network);
        }

        private void AddInlineStats(StringBuilder log)
        {
            if (this.walletManager.ContainsWallets)
                log.AppendLine("Wallet Height".PadRight(LoggingConfiguration.ColumnLength) + $": {this.walletManager.WalletTipHeight}".PadRight(10) + $"(Hash: {this.walletManager.WalletTipHash})");
            else
                log.AppendLine("Wallet Height".PadRight(LoggingConfiguration.ColumnLength) + ": No Wallet");
        }

        private void AddComponentStats(StringBuilder log)
        {
            List<string> walletNamesSQL = this.walletRepository.GetWalletNames();
            List<string> watchOnlyWalletNames = this.walletManager.GetWatchOnlyWalletsNames().ToList();

            if (walletNamesSQL.Any())
            {
                log.AppendLine(">> Wallets");

                var walletManager = (WalletManager)this.walletManager;

                foreach (string walletName in walletNamesSQL)
                {
                    string watchOnly = (watchOnlyWalletNames.Contains(walletName)) ? "(W) " : "";

                    try
                    {
                        foreach (AccountBalance accountBalance in walletManager.GetBalances(walletName))
                        {
                            log.AppendLine($"{watchOnly}{walletName}/{accountBalance.Account.Name}".PadRight(LoggingConfiguration.ColumnLength) + $": Confirmed balance: {accountBalance.AmountConfirmed}".PadRight(LoggingConfiguration.ColumnLength + 20) + $" Unconfirmed balance: {accountBalance.AmountUnconfirmed}");
                        }
                    }
                    catch (WalletException)
                    {
                        log.AppendLine("Can't access wallet balances, wallet might be in a process of rewinding or deleting.");
                    }
                }
            }
        }

        /// <inheritdoc />
        public override Task InitializeAsync()
        {
            this.walletManager.Start();
            this.walletSyncManager.Start();
            this.addressBookManager.Initialize();

            this.connectionManager.Parameters.TemplateBehaviors.Add(this.broadcasterBehavior);

            return Task.CompletedTask;
        }

        /// <inheritdoc />
        public override void Dispose()
        {
            this.walletSyncManager.Stop();
            this.walletManager.Stop();
        }
    }

    /// <summary>
    /// A class providing extension methods for <see cref="IFullNodeBuilder"/>.
    /// </summary>
    public static class FullNodeBuilderWalletExtension
    {
        public static IFullNodeBuilder UseWallet(this IFullNodeBuilder fullNodeBuilder)
        {
            LoggingConfiguration.RegisterFeatureNamespace<WalletFeature>("wallet");

            fullNodeBuilder.ConfigureFeature(features =>
            {
                features
                .AddFeature<WalletFeature>()
                .DependOn<MempoolFeature>()
                .DependOn<BlockStoreFeature>()
                .DependOn<RPCFeature>()
                .FeatureServices(services =>
                {
                    services.AddSingleton<IWalletService, WalletService>();
                    services.AddSingleton<IWalletSyncManager, WalletSyncManager>();
                    services.AddSingleton<IWalletTransactionHandler, WalletTransactionHandler>();
                    services.AddSingleton<IWalletManager, WalletManager>();
                    services.AddSingleton<IWalletFeePolicy, WalletFeePolicy>();
                    services.AddSingleton<IBroadcasterManager, FullNodeBroadcasterManager>();
                    services.AddSingleton<BroadcasterBehavior>();
                    services.AddSingleton<WalletSettings>();
                    services.AddSingleton<IScriptAddressReader>(new ScriptAddressReader());
                    services.AddSingleton<StandardTransactionPolicy>();
                    services.AddSingleton<IAddressBookManager, AddressBookManager>();
                    services.AddSingleton<IReserveUtxoService, ReserveUtxoService>();
                });
            });

            return fullNodeBuilder;
        }
    }
}