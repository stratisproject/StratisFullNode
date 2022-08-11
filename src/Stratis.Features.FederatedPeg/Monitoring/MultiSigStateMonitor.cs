using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using NBitcoin;
using NLog;
using Stratis.Bitcoin.AsyncWork;
using Stratis.Bitcoin.Features.PoA;
using Stratis.Bitcoin.Interfaces;
using Stratis.Bitcoin.Utilities;
using Stratis.Features.FederatedPeg.Distribution;
using Stratis.Features.FederatedPeg.Interfaces;
using Stratis.Features.FederatedPeg.TargetChain;

namespace Stratis.Features.FederatedPeg.Monitoring
{
    /// <summary>
    /// This class performs logic that periodically requests information from the other multisig members:
    /// 
    /// <para>
    /// 1. Ask each member the state of their <see cref="CrossChainTransferStore"/>.
    /// 
    /// Not replying to the payload within 5 minutes assumes that the member is not online or not connected to.
    /// </para>
    /// </summary>
    public sealed class MultiSigStateMonitor : IDisposable
    {
        private IAsyncLoop periodicStateRequest;
        private readonly IAsyncProvider asyncProvider;
        private readonly ChainIndexer chainIndexer;
        private readonly IFederationManager federationManager;
        private readonly IFederatedPegBroadcaster federatedPegBroadcaster;
        private readonly IInitialBlockDownloadState initialBlockDownloadState;
        private readonly ILogger logger;
        private readonly Network network;
        private readonly INodeLifetime nodeLifetime;

        public MultiSigStateMonitor(
            IAsyncProvider asyncProvider,
            ChainIndexer chainIndexer,
            IFederationManager federationManager,
            IFederatedPegBroadcaster federatedPegBroadcaster,
            IInitialBlockDownloadState initialBlockDownloadState,
            Network network,
            INodeLifetime nodeLifetime)
        {
            this.asyncProvider = asyncProvider;
            this.chainIndexer = chainIndexer;
            this.federationManager = federationManager;
            this.federatedPegBroadcaster = federatedPegBroadcaster;
            this.initialBlockDownloadState = initialBlockDownloadState;
            this.network = network;
            this.nodeLifetime = nodeLifetime;
            this.logger = LogManager.GetCurrentClassLogger();
        }

        /// <summary>
        /// Initializes the monitor, this starts the periodic async loop that requests information from the other multisig nodes.
        /// </summary>
        public void Initialize()
        {
            if (!this.federationManager.IsFederationMember)
            {
                this.logger.Warn("This node is not a federation member.");
                return;
            }

            this.periodicStateRequest = this.asyncProvider.CreateAndRunAsyncLoop(nameof(this.periodicStateRequest), async (cancellation) =>
            {
                if (this.initialBlockDownloadState.IsInitialBlockDownload())
                    return;

                try
                {
                    var multiSigMembers = new List<PubKey>();
                    if (this.network.IsTest())
                        multiSigMembers = MultiSigMembers.InteropMultisigContractPubKeysTestNet;
                    else
                        multiSigMembers = MultiSigMembers.InteropMultisigContractPubKeysMainNet;

                    // Iterate over all the multisig nodes.
                    foreach (PubKey multisigMember in multiSigMembers)
                    {
                        await BroadcastMultiSigStateRequestPayloadAsync(multisigMember.ToHex()).ConfigureAwait(false);
                    }
                }
                catch (Exception e)
                {
                    this.logger.Warn("Exception occurred when requesting state from other multisig nodes: {0}", e);
                }
            },
            this.nodeLifetime.ApplicationStopping,
            repeatEvery: TimeSpans.Minute,
            startAfter: TimeSpans.Minute);
        }

        private async Task BroadcastMultiSigStateRequestPayloadAsync(string memberToCheck)
        {
            // Include this multsig member's pubkey as signature so that the other node can check if the request was sent from a multisig member. 
            string signature = this.federationManager.CurrentFederationKey.SignMessage(memberToCheck);
            await this.federatedPegBroadcaster.BroadcastAsync(MultiSigMemberStateRequestPayload.Request(memberToCheck, signature)).ConfigureAwait(false);
            this.logger.Info($"Requesting state from multisig member '{memberToCheck}'");
        }

        /// <inheritdoc />
        public void Dispose()
        {
            this.periodicStateRequest?.Dispose();
        }
    }
}
