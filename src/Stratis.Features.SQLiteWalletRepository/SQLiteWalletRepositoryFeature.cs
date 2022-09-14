using System;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using NBitcoin;
using Stratis.Bitcoin.Builder;
using Stratis.Bitcoin.Builder.Feature;
using Stratis.Bitcoin.Features.Wallet.Interfaces;

namespace Stratis.Features.SQLiteWalletRepository
{
    public class SQLiteWalletRepositoryFeature : FullNodeFeature
    {        
        /// <summary>
        /// Initializes a new instance of the <see cref="SQLiteWalletRepositoryFeature"/> class.
        /// </summary>
        /// <param name="nodeSettings">The settings for the node.</param>
        /// <param name="loggerFactory">The factory used to create instance loggers.</param>
        public SQLiteWalletRepositoryFeature()
        {
        }

        /// <summary>
        /// Prints command-line help.
        /// </summary>
        /// <param name="network">The network to extract values from.</param>
        public static void PrintHelp(Network network)
        {
        }

        /// <summary>
        /// Get the default configuration.
        /// </summary>
        /// <param name="builder">The string builder to add the settings to.</param>
        /// <param name="network">The network to base the defaults off.</param>
        public static void BuildDefaultConfigurationFile(StringBuilder builder, Network network)
        {
        }

        /// <inheritdoc />
        public override Task InitializeAsync()
        {
            return Task.CompletedTask;
        }

        /// <inheritdoc />
        public override void Dispose()
        {
        }
    }

    /// <summary>
    /// A class providing extension methods for <see cref="IFullNodeBuilder"/>.
    /// </summary>
    /// <exception cref="InvalidOperationException">Thrown if this is not a Stratis network.</exception>
    public static class FullNodeBuilderSQLiteWalletRepositoryExtension
    {
        public static IFullNodeBuilder AddSQLiteWalletRepository(this IFullNodeBuilder fullNodeBuilder)
        {
            fullNodeBuilder.ConfigureFeature(features =>
            {
                features
                .AddFeature<SQLiteWalletRepositoryFeature>()
                .FeatureServices(services =>
                {
                    services.AddSingleton<IWalletRepository, SQLiteWalletRepository>();
                });
            });

            return fullNodeBuilder;
        }
    }
}
