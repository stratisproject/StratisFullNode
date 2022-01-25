using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using NBitcoin.Policy;
using Stratis.Bitcoin.Builder;
using Stratis.Bitcoin.Builder.Feature;
using Stratis.Bitcoin.Configuration.Logging;
using Stratis.Bitcoin.Consensus;
using Stratis.Bitcoin.Features.Wallet;
using Stratis.Bitcoin.Features.Wallet.Interfaces;
using Stratis.Bitcoin.Interfaces;

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

            logger.LogInformation("Smart Contract Feature Wallet Injected.");

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
                    // Registers the ScriptAddressReader concrete type and replaces the IScriptAddressReader implementation
                    // with SmartContractScriptAddressReader, which depends on the ScriptAddressReader concrete type.
                    services.AddSingleton<ScriptAddressReader>();
                    services.Replace(new ServiceDescriptor(typeof(IScriptAddressReader), typeof(SmartContractScriptAddressReader), ServiceLifetime.Singleton));

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