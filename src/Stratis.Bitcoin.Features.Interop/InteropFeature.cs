using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using NBitcoin;
using Stratis.Bitcoin.Builder;
using Stratis.Bitcoin.Builder.Feature;
using Stratis.Bitcoin.Configuration.Logging;
using Stratis.Bitcoin.Connection;
using Stratis.Bitcoin.Features.Interop.ETHClient;
using Stratis.Bitcoin.Features.Interop.Payloads;
using Stratis.Bitcoin.Features.Interop.Settings;
using Stratis.Bitcoin.Features.PoA;
using Stratis.Bitcoin.P2P.Peer;
using Stratis.Bitcoin.P2P.Protocol.Payloads;
using Stratis.Features.FederatedPeg.Conversion;
using Stratis.Features.FederatedPeg.Coordination;
using Stratis.Features.FederatedPeg.Payloads;

namespace Stratis.Bitcoin.Features.Interop
{
    /// <summary>
    /// A class containing all the related configuration to add chain interop functionality to the full node.
    /// </summary>
    public sealed class InteropFeature : FullNodeFeature
    {
        private readonly ChainIndexer chainIndexer;
        private readonly ICirrusContractClient cirrusClient;
        private readonly IConnectionManager connectionManager;
        private readonly IConversionRequestCoordinationService conversionRequestCoordinationService;
        private readonly IConversionRequestFeeService conversionRequestFeeService;
        private readonly IConversionRequestRepository conversionRequestRepository;
        private readonly IETHCompatibleClientProvider ethClientProvider;
        private readonly IFederationManager federationManager;
        private readonly InteropMonitor interopMonitor;
        private readonly InteropPoller interopPoller;
        private readonly InteropSettings interopSettings;
        private readonly Network network;

        public InteropFeature(
            ChainIndexer chainIndexer,
            ICirrusContractClient cirrusClient,
            IConnectionManager connectionManager,
            IConversionRequestCoordinationService conversionRequestCoordinationService,
            IConversionRequestFeeService conversionRequestFeeService,
            IConversionRequestRepository conversionRequestRepository,
            IETHCompatibleClientProvider ethCompatibleClientProvider,
            IFederationManager federationManager,
            IFullNode fullNode,
            InteropMonitor interopMonitor,
            InteropPoller interopPoller,
            InteropSettings interopSettings,
            Network network)
        {
            this.cirrusClient = cirrusClient;
            this.chainIndexer = chainIndexer;
            this.connectionManager = connectionManager;
            this.conversionRequestCoordinationService = conversionRequestCoordinationService;
            this.conversionRequestFeeService = conversionRequestFeeService;
            this.conversionRequestRepository = conversionRequestRepository;
            this.ethClientProvider = ethCompatibleClientProvider;
            this.federationManager = federationManager;
            this.interopMonitor = interopMonitor;
            this.interopPoller = interopPoller;
            this.interopSettings = interopSettings;
            this.network = network;

            var payloadProvider = (PayloadProvider)fullNode.Services.ServiceProvider.GetService(typeof(PayloadProvider));
            payloadProvider.AddPayload(typeof(ConversionRequestPayload));
            payloadProvider.AddPayload(typeof(FeeProposalPayload));
            payloadProvider.AddPayload(typeof(FeeAgreePayload));
            payloadProvider.AddPayload(typeof(ConversionRequestStatePayload));
        }

        /// <inheritdoc/>
        public override Task InitializeAsync()
        {
            // For now as only ethereum is supported we need set this to the quorum amount in the eth settings class.
            // Refactor this to a base.
            this.conversionRequestCoordinationService.RegisterConversionRequestQuorum(this.interopSettings.GetSettingsByChain(Wallet.DestinationChain.CIRRUS).MultisigWalletQuorum);
            this.conversionRequestCoordinationService.RegisterConversionRequestQuorum(this.interopSettings.GetSettingsByChain(Wallet.DestinationChain.ETH).MultisigWalletQuorum);

            this.interopPoller?.InitializeAsync();
            this.interopMonitor?.Initialize();

            NetworkPeerConnectionParameters networkPeerConnectionParameters = this.connectionManager.Parameters;
            networkPeerConnectionParameters.TemplateBehaviors.Add(new InteropBehavior(this.network, this.chainIndexer, this.cirrusClient, this.conversionRequestCoordinationService, this.conversionRequestFeeService, this.conversionRequestRepository, this.ethClientProvider, this.federationManager));

            return Task.CompletedTask;
        }

        /// <inheritdoc/>
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
                    .AddSingleton<IBNBClient, BNBClient>()
                    .AddSingleton<IETHCompatibleClientProvider, ETHCompatibleClientProvider>()
                    .AddSingleton<ICirrusContractClient, CirrusContractClient>()
                    .AddSingleton<InteropMonitor>()
                    .AddSingleton<InteropPoller>()
                    ));

            return fullNodeBuilder;
        }
    }
}
