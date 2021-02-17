using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NBitcoin;
using Stratis.Bitcoin.Builder;
using Stratis.Bitcoin.Builder.Feature;
using Stratis.Bitcoin.Configuration.Logging;
using Stratis.Bitcoin.Connection;
using Stratis.Bitcoin.Features.Interop.EthereumClient;
using Stratis.Bitcoin.Features.PoA;
using Stratis.Bitcoin.P2P.Peer;

namespace Stratis.Bitcoin.Features.Interop
{
    public sealed class InteropFeature : FullNodeFeature
    {
        private readonly ILoggerFactory loggerFactory;
        
        private readonly Network network;

        private readonly IFederationManager federationManager;

        private readonly IConnectionManager connectionManager;
        
        private readonly InteropPoller interopPoller;
        
        private readonly IInteropTransactionManager interopTransactionManager;
        
        private readonly IEthereumClientBase ethereumClientBase;

        public InteropFeature(ILoggerFactory loggerFactory, 
            Network network, 
            IFederationManager federationManager,
            IConnectionManager connectionManager,
            InteropPoller interopPoller,
            IInteropTransactionManager interopTransactionManager, 
            IEthereumClientBase ethereumClientBase)
        {
            this.loggerFactory = loggerFactory;
            this.network = network;
            this.federationManager = federationManager;
            this.connectionManager = connectionManager;
            this.interopPoller = interopPoller;
            this.interopTransactionManager = interopTransactionManager;
            this.ethereumClientBase = ethereumClientBase;
        }

        public override Task InitializeAsync()
        {
            this.interopPoller?.Initialize();

            NetworkPeerConnectionParameters networkPeerConnectionParameters = this.connectionManager.Parameters;
            networkPeerConnectionParameters.TemplateBehaviors.Add(new InteropBehavior(this.loggerFactory, this.network, this.federationManager, this.interopTransactionManager, this.ethereumClientBase));

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
