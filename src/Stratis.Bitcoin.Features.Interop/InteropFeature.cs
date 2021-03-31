using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using NBitcoin;
using Stratis.Bitcoin.Builder;
using Stratis.Bitcoin.Builder.Feature;
using Stratis.Bitcoin.Configuration.Logging;
using Stratis.Bitcoin.Connection;
using Stratis.Bitcoin.Features.Interop.EthereumClient;
using Stratis.Bitcoin.Features.Interop.Payloads;
using Stratis.Bitcoin.Features.PoA;
using Stratis.Bitcoin.P2P.Peer;
using Stratis.Bitcoin.P2P.Protocol.Payloads;

namespace Stratis.Bitcoin.Features.Interop
{
    public sealed class InteropFeature : FullNodeFeature
    {
        private readonly Network network;

        private readonly IFederationManager federationManager;

        private readonly IConnectionManager connectionManager;
        
        private readonly InteropPoller interopPoller;
        
        private readonly IInteropTransactionManager interopTransactionManager;
        
        private readonly IEthereumClientBase ethereumClientBase;

        public InteropFeature(
            Network network, 
            IFederationManager federationManager,
            IConnectionManager connectionManager,
            InteropPoller interopPoller,
            IInteropTransactionManager interopTransactionManager, 
            IEthereumClientBase ethereumClientBase,
            IFullNode fullNode)
        {
            this.network = network;
            this.federationManager = federationManager;
            this.connectionManager = connectionManager;
            this.interopPoller = interopPoller;
            this.interopTransactionManager = interopTransactionManager;
            this.ethereumClientBase = ethereumClientBase;

            var payloadProvider = (PayloadProvider)fullNode.Services.ServiceProvider.GetService(typeof(PayloadProvider));
            payloadProvider.AddPayload(typeof(InteropCoordinationPayload));
        }

        public override Task InitializeAsync()
        {
            this.interopPoller?.Initialize();

            NetworkPeerConnectionParameters networkPeerConnectionParameters = this.connectionManager.Parameters;
            networkPeerConnectionParameters.TemplateBehaviors.Add(new InteropBehavior(this.network, this.federationManager, this.interopTransactionManager, this.ethereumClientBase));

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
                    .AddSingleton<IInteropTransactionManager, InteropTransactionManager>()
                    .AddSingleton<InteropPoller>()
                    ));

            return fullNodeBuilder;
        }
    }
}
