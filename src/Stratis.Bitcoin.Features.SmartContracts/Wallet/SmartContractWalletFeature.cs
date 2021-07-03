using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using NBitcoin.Policy;
using NLog;
using Stratis.Bitcoin.Builder;
using Stratis.Bitcoin.Builder.Feature;
using Stratis.Bitcoin.Configuration.Logging;
using Stratis.Bitcoin.Features.Wallet;
using Stratis.Bitcoin.Features.Wallet.Interfaces;

namespace Stratis.Bitcoin.Features.SmartContracts.Wallet
{
    public sealed class SmartContractWalletFeature : FullNodeFeature
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="WalletFeature"/> class.
        /// </summary>
        public SmartContractWalletFeature()
        {
        }

        /// <inheritdoc />
        public override Task InitializeAsync()
        {
            ILogger logger = LogManager.GetCurrentClassLogger();

            logger.Info("Smart Contract Feature Wallet Injected.");

            return Task.CompletedTask;
        }

        /// <inheritdoc />
        public override void Dispose()
        {
        }
    }

    public static partial class IFullNodeBuilderExtensions
    {
        public static IFullNodeBuilder UseSmartContractWallet(this IFullNodeBuilder fullNodeBuilder, bool addVanillaWallet = true)
        {
            LoggingConfiguration.RegisterFeatureNamespace<WalletFeature>("smart contract wallet");

            if (addVanillaWallet)
                fullNodeBuilder.UseWallet();

            fullNodeBuilder.ConfigureFeature(features =>
            {
                features
                .AddFeature<SmartContractWalletFeature>()
                .DependOn<BaseWalletFeature>()
                .FeatureServices(services =>
                {
                    services.RemoveAll(typeof(StandardTransactionPolicy));
                    services.AddSingleton<StandardTransactionPolicy, SmartContractTransactionPolicy>();

                    services.RemoveAll(typeof(WalletTransactionHandler));
                    services.RemoveAll(typeof(IWalletTransactionHandler));
                    services.AddSingleton<IWalletTransactionHandler, SmartContractWalletTransactionHandler>();
                    
                    services.RemoveAll(typeof(ISmartContractTransactionService));
                    services.AddSingleton<ISmartContractTransactionService, SmartContractTransactionService>();

                    services.AddTransient<WalletRPCController>();
                });
            });

            return fullNodeBuilder;
        }
    }
}