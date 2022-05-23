using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using NLog;
using Stratis.Bitcoin.AsyncWork;
using Stratis.Bitcoin.Features.Interop.Payloads;
using Stratis.Bitcoin.Features.PoA;
using Stratis.Bitcoin.Interfaces;
using Stratis.Bitcoin.Utilities;
using Stratis.Features.FederatedPeg.Conversion;
using Stratis.Features.FederatedPeg.Interfaces;

namespace Stratis.Bitcoin.Features.Interop
{
    public sealed class InteropMonitor : IDisposable
    {
        private IAsyncLoop periodicCheckForProcessedRequests;
        private readonly IAsyncProvider asyncProvider;
        private readonly IConversionRequestRepository conversionRequestRepository;
        private readonly IFederationManager federationManager;
        private readonly IFederatedPegBroadcaster federatedPegBroadcaster;
        private readonly IInitialBlockDownloadState initialBlockDownloadState;
        private readonly ILogger logger;
        private readonly INodeLifetime nodeLifetime;

        public InteropMonitor(
            IAsyncProvider asyncProvider,
            IFederationManager federationManager,
            IInitialBlockDownloadState initialBlockDownloadState,
            INodeLifetime nodeLifetime)
        {
            this.asyncProvider = asyncProvider;
            this.federationManager = federationManager;
            this.initialBlockDownloadState = initialBlockDownloadState;
            this.nodeLifetime = nodeLifetime;
            this.logger = LogManager.GetCurrentClassLogger();
        }

        public void Initialize()
        {
            if (!this.federationManager.IsFederationMember)
            {
                this.logger.Warn("Not a federation member.");
                return;
            }

            // Initialize the interop polling loop, to check for interop contract requests.
            this.periodicCheckForProcessedRequests = this.asyncProvider.CreateAndRunAsyncLoop("periodicCheckForProcessedRequests", async (cancellation) =>
            {
                if (this.initialBlockDownloadState.IsInitialBlockDownload())
                    return;

                try
                {
                    // Request the state for all unprocessed mint requests.
                    List<ConversionRequest> mintRequests = this.conversionRequestRepository.GetAllMint(true);
                    if (mintRequests.Any())
                        this.logger.Info($"Requesting state for {mintRequests.Count} unprocessed mint requests.");

                    foreach (ConversionRequest request in mintRequests)
                    {
                        await this.BroadcastConversionRequestStatePayloadAsync(request).ConfigureAwait(false);
                    }

                    // Request the state for all unprocessed burn requests.
                    List<ConversionRequest> burnRequests = this.conversionRequestRepository.GetAllBurn(true);
                    if (burnRequests.Any())
                        this.logger.Info($"Requesting state for {burnRequests.Count} unprocessed burn requests.");

                    foreach (ConversionRequest request in burnRequests)
                    {
                        await this.BroadcastConversionRequestStatePayloadAsync(request).ConfigureAwait(false);
                    }
                }
                catch (Exception e)
                {
                    this.logger.Warn("Exception occurred when checking for processed conversion requests: {0}", e);
                }
            },
            this.nodeLifetime.ApplicationStopping,
            repeatEvery: TimeSpans.Minute,
            startAfter: TimeSpans.Minute);
        }

        private async Task BroadcastConversionRequestStatePayloadAsync(ConversionRequest conversionRequest)
        {
            string signature = this.federationManager.CurrentFederationKey.SignMessage(conversionRequest.RequestId);
            await this.federatedPegBroadcaster.BroadcastAsync(ConversionRequestStatePayload.Request(conversionRequest.RequestId, signature)).ConfigureAwait(false);
        }

        /// <inheritdoc />
        public void Dispose()
        {
            this.periodicCheckForProcessedRequests?.Dispose();
        }
    }
}
