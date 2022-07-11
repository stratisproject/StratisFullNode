using System;
using System.Numerics;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using NBitcoin;
using Stratis.Bitcoin.Configuration.Logging;
using Stratis.Bitcoin.Features.Interop.ETHClient;
using Stratis.Bitcoin.Features.Interop.Payloads;
using Stratis.Bitcoin.Features.PoA;
using Stratis.Bitcoin.P2P.Peer;
using Stratis.Bitcoin.P2P.Protocol;
using Stratis.Bitcoin.P2P.Protocol.Behaviors;
using Stratis.Features.FederatedPeg.Conversion;
using Stratis.Features.FederatedPeg.Coordination;
using Stratis.Features.FederatedPeg.Payloads;
using TracerAttributes;

namespace Stratis.Bitcoin.Features.Interop
{
    public sealed class InteropBehavior : NetworkPeerBehavior
    {
        private readonly ChainIndexer chainIndexer;
        private readonly ICirrusContractClient cirrusClient;
        private readonly IConversionRequestCoordinationService conversionRequestCoordinationService;
        private readonly IConversionRequestFeeService conversionRequestFeeService;
        private readonly IConversionRequestRepository conversionRequestRepository;
        private readonly IETHCompatibleClientProvider ethClientProvider;
        private readonly IFederationManager federationManager;
        private readonly ILogger logger;
        private readonly Network network;

        public InteropBehavior(
            Network network,
            ChainIndexer chainIndexer,
            ICirrusContractClient cirrusClient,
            IConversionRequestCoordinationService conversionRequestCoordinationService,
            IConversionRequestFeeService conversionRequestFeeService,
            IConversionRequestRepository conversionRequestRepository,
            IETHCompatibleClientProvider ethClientProvider,
            IFederationManager federationManager)
        {
            this.chainIndexer = chainIndexer;
            this.cirrusClient = cirrusClient;
            this.conversionRequestCoordinationService = conversionRequestCoordinationService;
            this.conversionRequestFeeService = conversionRequestFeeService;
            this.conversionRequestRepository = conversionRequestRepository;
            this.ethClientProvider = ethClientProvider;
            this.federationManager = federationManager;
            this.network = network;

            this.logger = LogManager.GetCurrentClassLogger();
        }

        /// <inheritdoc/>
        [NoTrace]
        public override object Clone()
        {
            return new InteropBehavior(this.network, this.chainIndexer, this.cirrusClient, this.conversionRequestCoordinationService, this.conversionRequestFeeService, this.conversionRequestRepository, this.ethClientProvider, this.federationManager);
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
                    case ConversionRequestPayload conversionRequestPayload:
                        await this.ProcessConversionRequestPayloadAsync(peer, conversionRequestPayload).ConfigureAwait(false);
                        break;

                    case FeeProposalPayload feeProposalPayload:
                        await this.ProcessFeeProposalAsync(feeProposalPayload).ConfigureAwait(false);
                        break;

                    case FeeAgreePayload feeAgreePayload:
                        await this.ProcessFeeAgreeAsync(feeAgreePayload).ConfigureAwait(false);
                        break;

                    case ConversionRequestStatePayload conversionRequestStatePayload:
                        await this.ProcessConversionRequestStateAsync(conversionRequestStatePayload).ConfigureAwait(false);
                        break;
                }
            }
            catch (OperationCanceledException)
            {
                this.logger.LogTrace("(-)[CANCELED_EXCEPTION]");
            }
        }

        private async Task ProcessConversionRequestPayloadAsync(INetworkPeer peer, ConversionRequestPayload payload)
        {
            this.logger.LogDebug($"Conversion request payload request for id '{payload.RequestId}' received from '{peer.PeerEndPoint.Address}':'{peer.RemoteSocketEndpoint.Address}' proposing transaction ID '{payload.TransactionId}', (IsTransfer: {payload.IsTransfer}, IsReplenishment: {payload.IsReplenishment}).");

            if (payload.TransactionId == BigInteger.MinusOne)
                return;

            // Check that the payload is signed by a multisig federation member.
            PubKey pubKey = RecoverPubKeyAndValidateMultiSigMember(payload.RequestId + payload.TransactionId, payload.RequestId, payload.Signature);
            if (pubKey == null)
                return;

            BigInteger confirmationCount;

            try
            {
                // Check that the transaction ID in the payload actually exists, and is unconfirmed.
                if (payload.IsTransfer)
                    confirmationCount = await this.cirrusClient.GetMultisigConfirmationCountAsync(payload.TransactionId, (ulong)this.chainIndexer.Tip.Height).ConfigureAwait(false);
                else
                    confirmationCount = await this.ethClientProvider.GetClientForChain(payload.DestinationChain).GetMultisigConfirmationCountAsync(payload.TransactionId).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                this.logger.LogError($"An exception occurred trying to retrieve the confirmation count for multisig transaction id '{payload.TransactionId}', request id'{payload.RequestId}': {ex}");
                return;
            }

            // We presume that the initial submitter of the transaction must have at least confirmed it. Otherwise just ignore this coordination attempt.
            if (confirmationCount < 1)
            {
                this.logger.LogInformation("Multisig wallet transaction {0} has no confirmations.", payload.TransactionId);
                return;
            }

            // Only add votes if the conversion request has not already been finalized or if it is a replenishment.
            ConversionRequest conversionRequest = this.conversionRequestRepository.Get(payload.RequestId);
            if ((conversionRequest != null && conversionRequest.RequestStatus != ConversionRequestStatus.VoteFinalised) || payload.IsReplenishment)
                this.conversionRequestCoordinationService.AddVote(payload.RequestId, payload.TransactionId, pubKey);

            if (payload.IsRequesting)
            {
                // Execute a small delay to prevent network congestion.
                await Task.Delay(TimeSpan.FromSeconds(1)).ConfigureAwait(false);

                string signature = this.federationManager.CurrentFederationKey.SignMessage(payload.RequestId + payload.TransactionId);
                await this.AttachedPeer.SendMessageAsync(ConversionRequestPayload.Reply(payload.RequestId, payload.TransactionId, signature, payload.DestinationChain, payload.IsTransfer, payload.IsReplenishment)).ConfigureAwait(false);
            }
        }

        private async Task ProcessFeeProposalAsync(FeeProposalPayload payload)
        {
            // Check that the payload is signed by a multisig federation member.
            PubKey pubKey = RecoverPubKeyAndValidateMultiSigMember(payload.RequestId + payload.FeeAmount, payload.RequestId, payload.Signature);
            if (pubKey == null)
                return;

            // Reply back to the peer with this node's proposal.
            FeeProposalPayload replyToPayload = await this.conversionRequestFeeService.MultiSigMemberProposedInteropFeeAsync(payload.RequestId, payload.FeeAmount, pubKey).ConfigureAwait(false);
            if (payload.IsRequesting && replyToPayload != null)
            {
                // Execute a small delay to prevent network congestion.
                await Task.Delay(TimeSpan.FromSeconds(1)).ConfigureAwait(false);

                await this.AttachedPeer.SendMessageAsync(replyToPayload).ConfigureAwait(false);
            }
        }

        private async Task ProcessFeeAgreeAsync(FeeAgreePayload payload)
        {
            // Check that the payload is signed by a multisig federation member.
            PubKey pubKey = RecoverPubKeyAndValidateMultiSigMember(payload.RequestId + payload.FeeAmount, payload.RequestId, payload.Signature);
            if (pubKey == null)
                return;

            // Reply back to the peer with this node's amount.
            FeeAgreePayload replyToPayload = await this.conversionRequestFeeService.MultiSigMemberAgreedOnInteropFeeAsync(payload.RequestId, payload.FeeAmount, pubKey).ConfigureAwait(false);
            if (payload.IsRequesting && replyToPayload != null)
            {
                // Execute a small delay to prevent network congestion.
                await Task.Delay(TimeSpan.FromSeconds(1)).ConfigureAwait(false);

                await this.AttachedPeer.SendMessageAsync(replyToPayload).ConfigureAwait(false);
            }
        }

        private async Task ProcessConversionRequestStateAsync(ConversionRequestStatePayload payload)
        {
            PubKey pubKey = RecoverPubKeyAndValidateMultiSigMember(payload.RequestId, payload.RequestId, payload.Signature);
            if (pubKey == null)
                return;

            ConversionRequest conversionRequest = this.conversionRequestRepository.Get(payload.RequestId);
            if (conversionRequest == null)
            {
                this.logger.LogDebug($"PubKey '{pubKey}' requested the state of request '{payload.RequestId}' but it does exist.");
                return;
            }

            if (payload.IsRequesting)
            {
                // Execute a small delay to prevent network congestion.
                await Task.Delay(TimeSpan.FromSeconds(2)).ConfigureAwait(false);
                string signature = this.federationManager.CurrentFederationKey.SignMessage(conversionRequest.RequestId);

                await this.AttachedPeer.SendMessageAsync(ConversionRequestStatePayload.Reply(conversionRequest.RequestId, conversionRequest.RequestStatus, signature)).ConfigureAwait(false);
            }
            else
            {
                // If the other node is replying with its state of the conversion request in question, then check and update ours.
                if (payload.RequestState == ConversionRequestStatus.Processed)
                {
                    conversionRequest.Processed = true;
                    conversionRequest.RequestStatus = ConversionRequestStatus.Processed;

                    this.conversionRequestRepository.Save(conversionRequest);

                    this.logger.LogDebug($"A conversion request with a processed state was received from PubKey '{pubKey}', updating ours.");
                }
            }
        }

        /// <summary>
        /// Check that the payload is signed by a multisig federation member.
        /// </summary>
        /// <param name="signatureText">The signature test to verify.</param>
        /// <param name="requestId">The conversion request in question.</param>
        /// <param name="signature">The signature to verify against.</param>
        /// <returns>A valid <see cref="PubKey"/> if signed by a multisig member.</returns>
        private PubKey RecoverPubKeyAndValidateMultiSigMember(string signatureText, string requestId, string signature)
        {
            PubKey pubKey;

            try
            {
                pubKey = PubKey.RecoverFromMessage(signatureText, signature);

                if (!this.federationManager.IsMultisigMember(pubKey))
                {
                    this.logger.LogWarning("Conversion request payload for '{0}'. Computed pubkey '{1}'.", requestId, pubKey?.ToHex());
                    return null;
                }
            }
            catch (Exception)
            {
                this.logger.LogWarning("Received malformed conversion request payload for '{0}'.", requestId);
                return null;
            }

            return pubKey;
        }
    }
}
