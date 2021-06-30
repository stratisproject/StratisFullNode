using System;
using System.Numerics;
using System.Threading.Tasks;
using NBitcoin;
using NLog;
using Stratis.Bitcoin.Features.Interop.ETHClient;
using Stratis.Bitcoin.Features.Interop.Payloads;
using Stratis.Bitcoin.Features.PoA;
using Stratis.Bitcoin.P2P.Peer;
using Stratis.Bitcoin.P2P.Protocol;
using Stratis.Bitcoin.P2P.Protocol.Behaviors;
using Stratis.Bitcoin.Utilities;
using Stratis.Features.FederatedPeg.Coordination;
using Stratis.Features.FederatedPeg.Payloads;
using TracerAttributes;

namespace Stratis.Bitcoin.Features.Interop
{
    public sealed class InteropBehavior : NetworkPeerBehavior
    {
        private readonly IConversionRequestCoordinationService conversionRequestCoordinationService;
        private readonly IConversionRequestFeeService conversionRequestFeeService;
        private readonly IETHClient ETHClient;
        private readonly IFederationManager federationManager;
        private readonly InteropSettings interopSettings;
        private readonly ILogger logger;
        private readonly Network network;

        public InteropBehavior(
            Network network,
            IFederationManager federationManager,
            IConversionRequestCoordinationService conversionRequestCoordinationService,
            IConversionRequestFeeService conversionRequestFeeService,
            IETHClient ETHClient,
            InteropSettings interopSettings)
        {
            Guard.NotNull(network, nameof(network));
            Guard.NotNull(federationManager, nameof(federationManager));
            Guard.NotNull(conversionRequestCoordinationService, nameof(conversionRequestCoordinationService));
            Guard.NotNull(ETHClient, nameof(ETHClient));
            Guard.NotNull(interopSettings, nameof(interopSettings));

            this.logger = LogManager.GetCurrentClassLogger();
            this.network = network;
            this.federationManager = federationManager;
            this.conversionRequestCoordinationService = conversionRequestCoordinationService;
            this.conversionRequestFeeService = conversionRequestFeeService;
            this.ETHClient = ETHClient;
            this.interopSettings = interopSettings;
        }

        [NoTrace]
        public override object Clone()
        {
            return new InteropBehavior(this.network, this.federationManager, this.conversionRequestCoordinationService, this.conversionRequestFeeService, this.ETHClient, this.interopSettings);
        }

        protected override void AttachCore()
        {
            this.logger.Debug("Attaching behaviour for {0}", this.AttachedPeer.PeerEndPoint.Address);
            this.AttachedPeer.MessageReceived.Register(this.OnMessageReceivedAsync, true);
        }

        protected override void DetachCore()
        {
            this.logger.Debug("Detaching behaviour for {0}", this.AttachedPeer.PeerEndPoint.Address);
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
                this.logger.Trace("(-)[CANCELED_EXCEPTION]");
                return;
            }
            catch (Exception ex)
            {
                this.logger.Error("Exception occurred: {0}", ex.ToString());
                throw;
            }
        }

        private async Task ProcessMessageAsync(INetworkPeer peer, IncomingMessage message)
        {
            try
            {
                switch (message.Message.Payload)
                {
                    case InteropCoordinationPayload interopCoordinationPayload:
                        await this.ProcessInteropCoordinationAsync(peer, interopCoordinationPayload).ConfigureAwait(false);
                        break;

                    case FeeProposalPayload feeProposalPayload:
                        await this.ProcessFeeProposalAsync(feeProposalPayload).ConfigureAwait(false);
                        break;

                    case FeeAgreePayload feeAgreePayload:
                        await this.ProcessFeeAgreeAsync(feeAgreePayload).ConfigureAwait(false);
                        break;
                }
            }
            catch (OperationCanceledException)
            {
                this.logger.Trace("(-)[CANCELED_EXCEPTION]");
            }
        }

        private async Task ProcessInteropCoordinationAsync(INetworkPeer peer, InteropCoordinationPayload payload)
        {
            if (!this.federationManager.IsFederationMember)
                return;

            this.logger.Debug("{0} received from '{1}':'{2}'. Request {3} proposing transaction ID {4}.", nameof(InteropCoordinationPayload), peer.PeerEndPoint.Address, peer.RemoteSocketEndpoint.Address, payload.RequestId, payload.TransactionId);

            if (payload.TransactionId == BigInteger.MinusOne)
                return;

            // Check that the payload is signed by a multisig federation member.
            PubKey pubKey;

            try
            {
                pubKey = PubKey.RecoverFromMessage(payload.RequestId + payload.TransactionId, payload.Signature);

                if (!this.federationManager.IsMultisigMember(pubKey))
                {
                    this.logger.Warn("Received unverified coordination payload for {0}. Computed pubkey {1}.", payload.RequestId, pubKey?.ToHex());

                    return;
                }
            }
            catch (Exception)
            {
                this.logger.Warn("Received malformed coordination payload for {0}.", payload.RequestId);

                return;
            }

            BigInteger confirmationCount;

            try
            {
                // Check that the transaction ID in the payload actually exists, and is unconfirmed.
                confirmationCount = await this.ETHClient.GetMultisigConfirmationCountAsync(payload.TransactionId).ConfigureAwait(false);
            }
            catch (Exception)
            {
                return;
            }

            // We presume that the initial submitter of the transaction must have at least confirmed it. Otherwise just ignore this coordination attempt.
            if (confirmationCount < 1)
            {
                this.logger.Info("Multisig wallet transaction {0} has no confirmations.", payload.TransactionId);

                return;
            }

            this.logger.Info("Multisig wallet transaction {0} has {1} confirmations (request ID: {2}).", payload.TransactionId, confirmationCount, payload.RequestId);

            this.conversionRequestCoordinationService.AddVote(payload.RequestId, payload.TransactionId, pubKey);
        }

        private async Task ProcessFeeProposalAsync(FeeProposalPayload payload)
        {
            if (!this.federationManager.IsFederationMember)
                return;

            // Check that the payload is signed by a multisig federation member.
            PubKey pubKey;

            try
            {
                pubKey = PubKey.RecoverFromMessage(payload.RequestId + payload.FeeAmount, payload.Signature);

                if (!this.federationManager.IsMultisigMember(pubKey))
                {
                    this.logger.Warn("Received unverified fee proposal payload for '{0}'. Computed pubkey {1}.", payload.RequestId, pubKey?.ToHex());
                    return;
                }
            }
            catch (Exception)
            {
                this.logger.Warn("Received malformed fee proposal payload for '{0}'.", payload.RequestId);
                return;
            }

            // Reply back to the peer with this node's amount.
            FeeProposalPayload replyToPayload = this.conversionRequestFeeService.MultiSigMemberProposedInteropFee(payload.RequestId, payload.FeeAmount, pubKey);
            if (replyToPayload != null)
                await this.AttachedPeer.SendMessageAsync(replyToPayload).ConfigureAwait(false);
        }

        private async Task ProcessFeeAgreeAsync(FeeAgreePayload payload)
        {
            if (!this.federationManager.IsFederationMember)
                return;

            // Check that the payload is signed by a multisig federation member.
            PubKey pubKey;

            try
            {
                pubKey = PubKey.RecoverFromMessage(payload.RequestId + payload.FeeAmount, payload.Signature);

                if (!this.federationManager.IsMultisigMember(pubKey))
                {
                    this.logger.Warn("Received unverified fee vote payload for '{0}'. Computed pubkey {1}.", payload.RequestId, pubKey?.ToHex());
                    return;
                }
            }
            catch (Exception)
            {
                this.logger.Warn("Received malformed fee vote payload for '{0}'.", payload.RequestId);
                return;
            }

            // Reply back to the peer with this node's amount.
            FeeAgreePayload replyToPayload = this.conversionRequestFeeService.MultiSigMemberAgreedOnInteropFee(payload.RequestId, payload.FeeAmount, pubKey);
            if (replyToPayload != null)
                await this.AttachedPeer.SendMessageAsync(replyToPayload).ConfigureAwait(false);
        }
    }
}
