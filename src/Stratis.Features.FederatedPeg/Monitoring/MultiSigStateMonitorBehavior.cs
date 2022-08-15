using System;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using NBitcoin;
using Stratis.Bitcoin.Configuration.Logging;
using Stratis.Bitcoin.EventBus.CoreEvents;
using Stratis.Bitcoin.Features.PoA;
using Stratis.Bitcoin.P2P.Peer;
using Stratis.Bitcoin.P2P.Protocol;
using Stratis.Bitcoin.P2P.Protocol.Behaviors;
using Stratis.Bitcoin.Signals;
using Stratis.Features.FederatedPeg.Interfaces;
using TracerAttributes;

namespace Stratis.Features.FederatedPeg.Monitoring
{
    public sealed class MultiSigStateMonitorBehavior : NetworkPeerBehavior
    {
        private readonly ICrossChainTransferStore crossChainTransferStore;
        private readonly IFederationManager federationManager;
        private readonly ILogger logger;
        private readonly Network network;
        private readonly ISignals signals;

        public MultiSigStateMonitorBehavior(
            Network network,
            ICrossChainTransferStore crossChainTransferStore,
            IFederationManager federationManager,
            ISignals signals)
        {
            this.crossChainTransferStore = crossChainTransferStore;
            this.federationManager = federationManager;
            this.network = network;
            this.signals = signals;

            this.logger = LogManager.GetCurrentClassLogger();
        }

        /// <inheritdoc/>
        [NoTrace]
        public override object Clone()
        {
            return new MultiSigStateMonitorBehavior(this.network, this.crossChainTransferStore, this.federationManager, this.signals);
        }

        /// <inheritdoc/>
        protected override void AttachCore()
        {
            this.logger.LogDebug("Attaching behaviour for {0}", this.AttachedPeer.PeerEndPoint.Address);
            this.AttachedPeer.MessageReceived.Register(this.OnMessageReceivedAsync, true);
        }

        /// <inheritdoc/>
        protected override void DetachCore()
        {
            this.logger.LogDebug("Detaching behaviour for {0}", this.AttachedPeer.PeerEndPoint.Address);
            this.AttachedPeer.MessageReceived.Unregister(this.OnMessageReceivedAsync);
        }

        private async Task OnMessageReceivedAsync(INetworkPeer peer, IncomingMessage message)
        {
            try
            {
                await this.ProcessMessageAsync(peer, message).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                this.logger.LogTrace("(-)[CANCELED_EXCEPTION]");
                return;
            }
            catch (Exception ex)
            {
                this.logger.LogError("Exception occurred: {0}", ex.ToString());
                throw;
            }
        }

        private async Task ProcessMessageAsync(INetworkPeer peer, IncomingMessage message)
        {
            if (!this.federationManager.IsFederationMember)
                return;

            try
            {
                switch (message.Message.Payload)
                {
                    case MultiSigMemberStateRequestPayload payload:
                        await this.ProcessMultiSigMemberStateRequestAsync(peer, payload).ConfigureAwait(false);
                        break;
                }
            }
            catch (OperationCanceledException)
            {
                this.logger.LogTrace("(-)[CANCELED_EXCEPTION]");
            }
        }

        private async Task ProcessMultiSigMemberStateRequestAsync(INetworkPeer peer, MultiSigMemberStateRequestPayload payload)
        {
            this.logger.LogDebug($"State request payload received for member '{payload.MemberToCheck}' from '{peer.PeerEndPoint.Address}':'{peer.RemoteSocketEndpoint.Address}'.");

            // Check that the payload is signed by a multisig federation member.
            PubKey pubKey = RecoverPubKeyAndValidateMultiSigMember(payload.MemberToCheck, payload.Signature);
            if (pubKey == null)
                return;

            if (payload.IsRequesting)
            {
                // Execute a small delay to prevent network congestion.
                await Task.Delay(TimeSpan.FromSeconds(1)).ConfigureAwait(false);

                string signature = this.federationManager.CurrentFederationKey.SignMessage(this.federationManager.CurrentFederationKey.PubKey.ToHex());
                var reply = MultiSigMemberStateRequestPayload.Reply(this.federationManager.CurrentFederationKey.PubKey.ToHex(), signature);
                reply.CrossChainStoreHeight = this.crossChainTransferStore.TipHashAndHeight.Height;
                reply.CrossChainStoreNextDepositHeight = this.crossChainTransferStore.NextMatureDepositHeight;
                reply.PartialTransactions = this.crossChainTransferStore.GetTransfersByStatus(new[] { CrossChainTransferStatus.Partial }).Length;
                reply.SuspendedPartialTransactions = this.crossChainTransferStore.GetTransfersByStatus(new[] { CrossChainTransferStatus.Suspended }).Length;

                await this.AttachedPeer.SendMessageAsync(reply).ConfigureAwait(false);
            }
            else
            {
                // Publish the results
                this.signals.Publish(new MultiSigMemberStateRequestEvent()
                {
                    PubKey = this.federationManager.CurrentFederationKey.PubKey.ToHex(),

                    CrossChainStoreHeight = payload.CrossChainStoreHeight,
                    CrossChainStoreNextDepositHeight = payload.CrossChainStoreNextDepositHeight,
                    PartialTransactions = payload.PartialTransactions,
                    SuspendedPartialTransactions = payload.SuspendedPartialTransactions,
                });
            }
        }

        /// <summary>
        /// Check that the payload is signed by a multisig federation member.
        /// </summary>
        /// <param name="signatureText">The signature test to verify.</param>
        /// <param name="signature">The signature to verify against.</param>
        /// <returns>A valid <see cref="PubKey"/> if signed by a multisig member.</returns>
        private PubKey RecoverPubKeyAndValidateMultiSigMember(string signatureText, string signature)
        {
            PubKey pubKey;

            try
            {
                pubKey = PubKey.RecoverFromMessage(signatureText, signature);

                if (!this.federationManager.IsMultisigMember(pubKey))
                {
                    this.logger.LogWarning($"'{pubKey?.ToHex()}' is not a multisig member.");
                    return null;
                }
            }
            catch (Exception)
            {
                this.logger.LogWarning($"Received malformed payload signature for member '{signatureText}'.");
                return null;
            }

            return pubKey;
        }
    }
}
