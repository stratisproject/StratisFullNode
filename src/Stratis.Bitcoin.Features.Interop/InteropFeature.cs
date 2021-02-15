using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Stratis.Bitcoin.Builder;
using Stratis.Bitcoin.Builder.Feature;
using Stratis.Bitcoin.Configuration.Logging;
using Stratis.Bitcoin.Features.Interop.EthereumClient;

namespace Stratis.Bitcoin.Features.Interop
{
    public sealed class InteropFeature : FullNodeFeature
    {
        private readonly InteropPoller interopPoller;

        public InteropFeature(InteropPoller interopPoller)
        {
            this.interopPoller = interopPoller;
        }

        public override Task InitializeAsync()
        {
            this.interopPoller?.Initialize();

            return Task.CompletedTask;
        }

        public override void Dispose()
        {
            this.interopPoller?.Dispose();
        }
    }

    public static partial class IFullNodeBuilderExtensions
    {
        public static IFullNodeBuilder AddInteroperability(this IFullNodeBuilder fullNodeBuilder)
        {
            LoggingConfiguration.RegisterFeatureNamespace<InteropFeature>("interop");

            fullNodeBuilder.ConfigureFeature(features =>
                features
                    .AddFeature<InteropFeature>()
                    .FeatureServices(services => services
                    .AddSingleton<InteropSettings>()
                    .AddSingleton<IEthereumClientBase, EthereumClientBase>()
                    //.AddSingleton<IInteropRequestRepository, InteropRequestRepository>()
                    //.AddSingleton<IInteropRequestKeyValueStore, InteropRequestKeyValueStore>()
                    .AddSingleton<IInteropTransactionManager, InteropTransactionManager>()
                    .AddSingleton<InteropPoller>()
                    ));

            return fullNodeBuilder;
        }
    }
}
