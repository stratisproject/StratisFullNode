using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using NBitcoin;
using Stratis.Bitcoin.Builder;
using Stratis.Bitcoin.Builder.Feature;
using Stratis.Bitcoin.Configuration.Logging;
using Stratis.Bitcoin.Connection;
using Stratis.Bitcoin.Features.Interop.ETHClient;
using Stratis.Bitcoin.Features.Interop.Payloads;
using Stratis.Bitcoin.Features.PoA;
using Stratis.Bitcoin.P2P.Peer;
using Stratis.Bitcoin.P2P.Protocol.Payloads;
using Stratis.Features.FederatedPeg.Coordination;
using Stratis.Features.FederatedPeg.Payloads;

namespace Stratis.Bitcoin.Features.Interop
{
    public sealed class InteropFeature : FullNodeFeature
    {
        private readonly Network network;

        private readonly IFederationManager federationManager;

        private readonly IConnectionManager connectionManager;

        private readonly InteropPoller interopPoller;

        private readonly IConversionRequestCoordinationService conversionRequestCoordinationService;
        private readonly IConversionRequestFeeService conversionRequestFeeService;

        private readonly IETHClient ethereumClientBase;

        private readonly InteropSettings interopSettings;

        public InteropFeature(
            Network network,
            IFederationManager federationManager,
            IConnectionManager connectionManager,
            InteropPoller interopPoller,
            IConversionRequestCoordinationService conversionRequestCoordinationService,
            IConversionRequestFeeService conversionRequestFeeService,
            IETHClient ethereumClientBase,
            InteropSettings interopSettings,
            IFullNode fullNode)
        {
            this.network = network;
            this.federationManager = federationManager;
            this.connectionManager = connectionManager;
            this.interopPoller = interopPoller;
            this.conversionRequestCoordinationService = conversionRequestCoordinationService;
            this.conversionRequestFeeService = conversionRequestFeeService;
            this.ethereumClientBase = ethereumClientBase;
            this.interopSettings = interopSettings;

            var payloadProvider = (PayloadProvider)fullNode.Services.ServiceProvider.GetService(typeof(PayloadProvider));
            payloadProvider.AddPayload(typeof(InteropCoordinationPayload));
            payloadProvider.AddPayload(typeof(FeeProposalPayload));
            payloadProvider.AddPayload(typeof(FeeAgreePayload));
        }

        public override Task InitializeAsync()
        {
            this.conversionRequestCoordinationService.RegisterConversionRequestQuorum(this.interopSettings.ETHMultisigWalletQuorum);

            this.interopPoller?.Initialize();

            NetworkPeerConnectionParameters networkPeerConnectionParameters = this.connectionManager.Parameters;
            networkPeerConnectionParameters.TemplateBehaviors.Add(new InteropBehavior(this.network, this.federationManager, this.conversionRequestCoordinationService, this.conversionRequestFeeService, this.ethereumClientBase, this.interopSettings));

            return Task.CompletedTask;
        }

        public override void Dispose()
        {
            this.interopPoller?.Dispose();
        }
    }

    public static partial class IFullNodeBuilderExtensions
    {
        /// <summary>
        /// Adds chain Interoperability to the node.
        /// </summary>
        /// <param name="fullNodeBuilder">The full node builder instance.</param>
        public static IFullNodeBuilder AddInteroperability(this IFullNodeBuilder fullNodeBuilder)
        {
            LoggingConfiguration.RegisterFeatureNamespace<InteropFeature>("interop");

            fullNodeBuilder.ConfigureFeature(features =>
                features
                    .AddFeature<InteropFeature>()
                    .FeatureServices(services => services
                        .AddSingleton<InteropSettings>()
                        .AddSingleton<IETHClient, ETHClient.ETHClient>()
                        .AddSingleton<InteropPoller>()
                    ));

            return fullNodeBuilder;
        }
    }
}
