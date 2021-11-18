using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Stratis.Bitcoin.Builder;
using Stratis.Bitcoin.Builder.Feature;
using Stratis.Bitcoin.Configuration.Logging;

namespace Stratis.Bitcoin.Features.ExternalApi
{
    /// <summary>
    /// The External API Feature.
    /// </summary>
    public sealed class ExternalApiFeature : FullNodeFeature
    {
        private readonly IExternalApiPoller externalApiPoller;
        
        /// <summary>
        /// The class constructor.
        /// </summary>
        /// <param name="externalApiPoller">The <see cref="IExternalApiPoller"/>.</param>
        public ExternalApiFeature(IExternalApiPoller externalApiPoller)
        {
            this.externalApiPoller = externalApiPoller;
        }

        /// <summary>
        /// Initializes the instance.
        /// </summary>
        /// <returns>The asynchronous task.</returns>
        public override Task InitializeAsync()
        {
            this.externalApiPoller?.Initialize();

            return Task.CompletedTask;
        }

        /// <summary>
        /// Disposes the instance.
        /// </summary>
        public override void Dispose()
        {
            this.externalApiPoller?.Dispose();
        }
    }

    /// <summary>
    /// <see cref="IFullNodeBuilder"/> extensions.
    /// </summary>
    public static partial class IFullNodeBuilderExtensions
    {
        /// <summary>
        /// Add the External Api feature to the node.
        /// </summary>
        /// <param name="fullNodeBuilder">Implicitly passed <see cref="IFullNodeBuilder"/>.</param>
        /// <returns>The <see cref="IFullNodeBuilder"/>.</returns>
        public static IFullNodeBuilder AddExternalApi(this IFullNodeBuilder fullNodeBuilder)
        {
            LoggingConfiguration.RegisterFeatureNamespace<ExternalApiFeature>("externalapi");

            fullNodeBuilder.ConfigureFeature(features =>
                features
                    .AddFeature<ExternalApiFeature>()
                    .FeatureServices(services => services
                    .AddSingleton<ExternalApiSettings>()
                    .AddSingleton<IExternalApiPoller, ExternalApiPoller>()
                    ));

            return fullNodeBuilder;
        }
    }
}
