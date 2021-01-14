﻿using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using NBitcoin;
using Stratis.Bitcoin.P2P.Peer;
using Stratis.Bitcoin.P2P.Protocol;
using Stratis.Bitcoin.P2P.Protocol.Behaviors;
using Stratis.Bitcoin.Utilities;
using Stratis.Features.FederatedPeg.InputConsolidation;
using Stratis.Features.FederatedPeg.Interfaces;
using Stratis.Features.FederatedPeg.Payloads;
using TracerAttributes;

namespace Stratis.Features.FederatedPeg
{
    public class PartialTransactionsBehavior : NetworkPeerBehavior
    {
        private readonly ILoggerFactory loggerFactory;

        private readonly ILogger logger;

        private readonly IFederationWalletManager federationWalletManager;

        private readonly Network network;

        private readonly IFederatedPegSettings federatedPegSettings;

        private readonly ICrossChainTransferStore crossChainTransferStore;

        private readonly IInputConsolidator inputConsolidator;

        public PartialTransactionsBehavior(
            ILoggerFactory loggerFactory,
            IFederationWalletManager federationWalletManager,
            Network network,
            IFederatedPegSettings federatedPegSettings,
            ICrossChainTransferStore crossChainTransferStore,
            IInputConsolidator inputConsolidator)
        {
            Guard.NotNull(loggerFactory, nameof(loggerFactory));
            Guard.NotNull(federationWalletManager, nameof(federationWalletManager));
            Guard.NotNull(network, nameof(network));
            Guard.NotNull(federatedPegSettings, nameof(federatedPegSettings));
            Guard.NotNull(crossChainTransferStore, nameof(crossChainTransferStore));

            this.logger = loggerFactory.CreateLogger(this.GetType().FullName);
            this.loggerFactory = loggerFactory;
            this.federationWalletManager = federationWalletManager;
            this.network = network;
            this.federatedPegSettings = federatedPegSettings;
            this.crossChainTransferStore = crossChainTransferStore;
            this.inputConsolidator = inputConsolidator;
        }

        [NoTrace]
        public override object Clone()
        {
            return new PartialTransactionsBehavior(this.loggerFactory, this.federationWalletManager, this.network,
                this.federatedPegSettings, this.crossChainTransferStore, this.inputConsolidator);
        }

        protected override void AttachCore()
        {
            this.logger.LogDebug("Attaching behaviour for {0}", this.AttachedPeer.PeerEndPoint.Address);
            this.AttachedPeer.MessageReceived.Register(this.OnMessageReceivedAsync, true);
        }

        protected override void DetachCore()
        {
            this.logger.LogDebug("Detaching behaviour for {0}", this.AttachedPeer.PeerEndPoint.Address);
            this.AttachedPeer.MessageReceived.Unregister(this.OnMessageReceivedAsync);
        }

        /// <summary>
        /// Broadcast the partial transaction request to federation members.
        /// </summary>
        /// <param name="payload">The payload to broadcast.</param>
        private async Task BroadcastAsync(RequestPartialTransactionPayload payload)
        {
            this.logger.LogInformation("Broadcasting to {0}", this.AttachedPeer.PeerEndPoint.Address);
            await this.AttachedPeer.SendMessageAsync(payload).ConfigureAwait(false);
        }

        private async Task OnMessageReceivedAsync(INetworkPeer peer, IncomingMessage message)
        {
            if (!(message.Message.Payload is RequestPartialTransactionPayload payload))
                return;

            // Is a consolidation request.
            if (payload.DepositId == RequestPartialTransactionPayload.ConsolidationDepositId)
            {
                this.logger.LogDebug("Received request to sign consolidation transaction.");
                await this.HandleConsolidationTransactionRequestAsync(peer, payload);
                return;
            }

            this.logger.LogInformation("{0} received from '{1}':'{2}'.", nameof(RequestPartialTransactionPayload), peer.PeerEndPoint.Address, peer.RemoteSocketEndpoint.Address);

            ICrossChainTransfer[] transfer = await this.crossChainTransferStore.GetAsync(new[] { payload.DepositId });

            // This could be null if the store was unable to sync with the federation 
            // wallet manager. It is possible that the federation wallet's tip is not 
            // on chain and as such the store was not able to sync.
            if (transfer == null)
            {
                this.logger.LogInformation("{0}: Unable to retrieve transfers for deposit {1} at this time, the store is not synced.", nameof(this.OnMessageReceivedAsync), payload.DepositId);
                return;
            }

            if (transfer[0] == null)
            {
                this.logger.LogInformation("{0}: Deposit {1} does not exist.", nameof(this.OnMessageReceivedAsync), payload.DepositId);
                return;
            }

            if (transfer[0].Status != CrossChainTransferStatus.Partial)
            {
                this.logger.LogInformation("{0}: Deposit {1} is {2}.", nameof(this.OnMessageReceivedAsync), payload.DepositId, transfer[0].Status);
                return;
            }

            if (transfer[0].PartialTransaction == null)
            {
                this.logger.LogInformation("{0}: Deposit {1}, PartialTransaction not found.", nameof(this.OnMessageReceivedAsync), payload.DepositId);
                return;
            }

            uint256 oldHash = transfer[0].PartialTransaction.GetHash();

            Transaction signedTransaction = this.crossChainTransferStore.MergeTransactionSignatures(payload.DepositId, new[] { payload.PartialTransaction });

            if (signedTransaction == null)
            {
                this.logger.LogInformation("{0}: Deposit {1}, signedTransaction not found.", nameof(this.OnMessageReceivedAsync), payload.DepositId);
                return;
            }

            if (oldHash != signedTransaction.GetHash())
            {
                this.logger.LogInformation("Signed transaction (deposit={0}) to produce {1} from {2}.", payload.DepositId, signedTransaction.GetHash(), oldHash);

                // Respond back to the peer that requested a signature.
                await this.BroadcastAsync(payload.AddPartial(signedTransaction));
            }
            else
            {
                this.logger.LogInformation("The old and signed hash matches '{0}'.", oldHash);
            }
        }

        private async Task HandleConsolidationTransactionRequestAsync(INetworkPeer peer, RequestPartialTransactionPayload payload)
        {
            ConsolidationSignatureResult result = this.inputConsolidator.CombineSignatures(payload.PartialTransaction);

            if (result.Signed)
            {
                this.logger.LogDebug("Signed consolidating transaction to produce {0} from {1}", result.TransactionResult.GetHash(), payload.PartialTransaction.GetHash());
                await this.BroadcastAsync(payload.AddPartial(result.TransactionResult));
            }
        }
    }
}
