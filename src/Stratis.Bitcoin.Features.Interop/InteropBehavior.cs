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
    public class InteropBehavior : NetworkPeerBehavior
    {
        private readonly ILogger logger;

        private readonly Network network;

        private readonly IFederationManager federationManager;

        private readonly ICoordinationManager coordinationManager;

        private readonly IETHClient ETHClient;

        private readonly InteropSettings interopSettings;

        public InteropBehavior(Network network, IFederationManager federationManager, ICoordinationManager coordinationManager, IETHClient ETHClient, InteropSettings interopSettings)
        {
            Guard.NotNull(network, nameof(network));
            Guard.NotNull(federationManager, nameof(federationManager));
            Guard.NotNull(coordinationManager, nameof(coordinationManager));
            Guard.NotNull(ETHClient, nameof(ETHClient));
            Guard.NotNull(interopSettings, nameof(interopSettings));

            this.logger = LogManager.GetCurrentClassLogger();
            this.network = network;
            this.federationManager = federationManager;
            this.coordinationManager = coordinationManager;
            this.ETHClient = ETHClient;
            this.interopSettings = interopSettings;
        }

        [NoTrace]
        public override object Clone()
        {
            return new InteropBehavior(this.network, this.federationManager, this.coordinationManager, this.ETHClient, this.interopSettings);
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
                        await this.ProcessFeeProposalAsync(peer, feeProposalPayload).ConfigureAwait(false);
                        break;

                    case FeeAgreePayload feeAgreePayload:
                        await this.ProcessFeeAgreeAsync(peer, feeAgreePayload).ConfigureAwait(false);
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

            this.logger.Info("{0} received from '{1}':'{2}'. Request {3} proposing transaction ID {4}.", nameof(InteropCoordinationPayload), peer.PeerEndPoint.Address, peer.RemoteSocketEndpoint.Address, payload.RequestId, payload.TransactionId);

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
                confirmationCount = await this.ETHClient.GetConfirmationCountAsync(payload.TransactionId).ConfigureAwait(false);
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

            this.coordinationManager.AddVote(payload.RequestId, payload.TransactionId, pubKey);
        }

        private async Task ProcessFeeProposalAsync(INetworkPeer peer, FeeProposalPayload payload)
        {
            if (!this.federationManager.IsFederationMember)
                return;

            // Check that the payload is signed by a multisig federation member.
            PubKey pubKey;

            try
            {
                pubKey = PubKey.RecoverFromMessage(payload.RequestId + payload.FeeAmount, payload.Signature);

                this.logger.Info($"Fee proposal payload received from PubKey '{pubKey}' for proposal '{payload.RequestId}'.");

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

            this.coordinationManager.MultiSigMemberProposedInteropFee(payload.RequestId, payload.FeeAmount, payload.Height, pubKey);
        }

        private async Task ProcessFeeAgreeAsync(INetworkPeer peer, FeeAgreePayload payload)
        {
            if (!this.federationManager.IsFederationMember)
                return;

            // Check that the payload is signed by a multisig federation member.
            PubKey pubKey;

            try
            {
                pubKey = PubKey.RecoverFromMessage(payload.RequestId + payload.FeeAmount, payload.Signature);

                this.logger.Info($"Fee agreed vote payload received from PubKey '{pubKey}' for request '{payload.RequestId}'.");

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

            this.coordinationManager.MultiSigMemberAgreedOnInteropFee(payload.RequestId, payload.FeeAmount, payload.Height, pubKey);
        }
    }
}
