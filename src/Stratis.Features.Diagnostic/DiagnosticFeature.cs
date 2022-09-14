using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using NBitcoin;
using Stratis.Bitcoin.Builder;
using Stratis.Bitcoin.Builder.Feature;
using Stratis.Bitcoin.Configuration.Logging;
using Stratis.Bitcoin.Signals;
using Stratis.Bitcoin.Utilities;
using Stratis.Features.Diagnostic.Controllers;
using Stratis.Features.Diagnostic.PeerDiagnostic;

namespace Stratis.Features.Diagnostic
{
    /// <summary>
    /// Feature for diagnostic purpose that allow to have insights about internal details of the fullnode while it's running.
    /// <para>In order to collect internal details, this feature makes use of Signals to register to internal events published
    /// by the full node and uses reflection whenever it needs to access information not meant to be publicly exposed.</para>
    /// <para>It exposes <see cref="DiagnosticController"/>, an API controller that allow to query for information using the API feature, when available.</para>
    /// </summary>
    /// <seealso cref="Stratis.Bitcoin.Builder.Feature.FullNodeFeature" />
    public class DiagnosticFeature : FullNodeFeature
    {
        private readonly ISignals signals;
        private readonly DiagnosticSettings diagnosticSettings;
        private readonly PeerStatisticsCollector peerStatisticsCollector;

        /// <summary>
        /// The class instance constructor.
        /// </summary>
        /// <param name="signals">See <see cref="ISignals"/>.</param>
        /// <param name="diagnosticSettings">See <see cref="DiagnosticSettings"/>.</param>
        /// <param name="peerStatisticsCollector">See <see cref="PeerStatisticsCollector"/>.</param>
        public DiagnosticFeature(ISignals signals, DiagnosticSettings diagnosticSettings, PeerStatisticsCollector peerStatisticsCollector)
        {
            this.signals = Guard.NotNull(signals, nameof(signals));
            this.diagnosticSettings = Guard.NotNull(diagnosticSettings, nameof(diagnosticSettings));
            this.peerStatisticsCollector = Guard.NotNull(peerStatisticsCollector, nameof(peerStatisticsCollector));
        }

        /// <summary>
        /// Initializes the instance.
        /// </summary>
        /// <returns>The asynchronous task.</returns>
        public override Task InitializeAsync()
        {
            this.peerStatisticsCollector.Initialize();
            return Task.CompletedTask;
        }

        /// <summary>
        /// Prints command-line help.
        /// </summary>
        /// <param name="network">The network to extract values from.</param>
        public static void PrintHelp(Network network)
        {
            DiagnosticSettings.PrintHelp(network);
        }

        /// <summary>
        /// Disposes the instance.
        /// </summary>
        public override void Dispose()
        {
            this.peerStatisticsCollector.Dispose();
        }
    }

    /// <summary>
    /// Extension for adding the feature to the node.
    /// </summary>
    public static class DiagnosticFeatureExtension
    {
        /// <summary>
        /// Adds the feature to the node.
        /// </summary>
        /// <param name="fullNodeBuilder">See <see cref="IFullNodeBuilder"/>.</param>
        /// <returns>The <see cref="IFullNodeBuilder"/>.</returns>
        public static IFullNodeBuilder UseDiagnosticFeature(this IFullNodeBuilder fullNodeBuilder)
        {
            LoggingConfiguration.RegisterFeatureNamespace<DiagnosticFeature>("diagnostic");

            fullNodeBuilder.ConfigureFeature(features =>
                features
                .AddFeature<DiagnosticFeature>()
                .FeatureServices(services => services
                    .AddSingleton<DiagnosticController>()
                    .AddSingleton<PeerStatisticsCollector>()
                    .AddSingleton<DiagnosticSettings>()
                )
            );

            return fullNodeBuilder;
        }
    }
}
