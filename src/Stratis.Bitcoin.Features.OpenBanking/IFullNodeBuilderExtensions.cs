using Microsoft.Extensions.DependencyInjection;
using Stratis.Bitcoin.Builder;
using Stratis.Bitcoin.Configuration.Logging;
using Stratis.Bitcoin.Features.OpenBanking.OpenBanking;
using Stratis.Bitcoin.Features.OpenBanking.TokenMinter;
using Stratis.Bitcoin.Features.SmartContracts.MetadataTracker;
using Stratis.SmartContracts.Core.Receipts;

namespace Stratis.Bitcoin.Features.OpenBanking
{
    /// <summary>
    /// Extension methods for <see cref="IFullNodeBuilder"/>.
    /// </summary>
    public static class IFullNodeBuilderExtensions
    {
        /// <summary>
        /// Configures the OpenBanking feature.
        /// </summary>
        /// <param name="fullNodeBuilder">Full node builder used to configure the feature.</param>
        /// <returns>The full node builder with the Dns feature configured.</returns>
        public static IFullNodeBuilder UseOpenBanking(this IFullNodeBuilder fullNodeBuilder)
        {
            LoggingConfiguration.RegisterFeatureNamespace<OpenBankingFeature>("ob");

            fullNodeBuilder.ConfigureFeature(features =>
            {
                features
                .AddFeature<OpenBankingFeature>()

                .FeatureServices(services =>
                {
                    services.AddSingleton(fullNodeBuilder);
                    services.AddSingleton<OpenBankingSettings>();
                    services.AddSingleton<ReceiptSearcher>();
                    services.AddSingleton<IMetadataTracker, MetaDataTracker>();
                    services.AddSingleton<IOpenBankingService, OpenBankingService>();
                    services.AddSingleton<ITokenMintingTransactionBuilder, TokenMintingTransactionBuilder>();
                    services.AddSingleton<ITokenMintingService, TokenMintingService>();
                });
            });

            return fullNodeBuilder;
        }
    }
}
