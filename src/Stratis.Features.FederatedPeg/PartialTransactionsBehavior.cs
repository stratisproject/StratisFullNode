using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using NBitcoin;
using Stratis.Bitcoin.Configuration.Logging;
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
    public sealed class PartialTransactionsBehavior : NetworkPeerBehavior
    {
        private readonly ILogger logger;

        private readonly IFederationWalletManager federationWalletManager;

        private readonly Network network;

        private readonly IFederatedPegSettings federatedPegSettings;

        private readonly ICrossChainTransferStore crossChainTransferStore;

        private readonly IInputConsolidator inputConsolidator;

        public PartialTransactionsBehavior(
            IFederationWalletManager federationWalletManager,
            Network network,
            IFederatedPegSettings federatedPegSettings,
            ICrossChainTransferStore crossChainTransferStore,
            IInputConsolidator inputConsolidator)
        {
            Guard.NotNull(federationWalletManager, nameof(federationWalletManager));
            Guard.NotNull(network, nameof(network));
            Guard.NotNull(federatedPegSettings, nameof(federatedPegSettings));
            Guard.NotNull(crossChainTransferStore, nameof(crossChainTransferStore));

            this.logger = LogManager.GetCurrentClassLogger();
            this.federationWalletManager = federationWalletManager;
            this.network = network;
            this.federatedPegSettings = federatedPegSettings;
            this.crossChainTransferStore = crossChainTransferStore;
            this.inputConsolidator = inputConsolidator;
        }

        [NoTrace]
        public override object Clone()
        {
            return new PartialTransactionsBehavior(this.federationWalletManager, this.network, this.federatedPegSettings,
                this.crossChainTransferStore, this.inputConsolidator);
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

        /// <summary>
        /// Broadcast the partial transaction request to federation members.
        /// </summary>
        /// <param name="payload">The payload to broadcast.</param>
        private async Task BroadcastAsync(RequestPartialTransactionPayload payload)
        {
            this.logger.Debug("Broadcasting to {0}", this.AttachedPeer.PeerEndPoint.Address);
            await this.AttachedPeer.SendMessageAsync(payload).ConfigureAwait(false);
        }

        private async Task OnMessageReceivedAsync(INetworkPeer peer, IncomingMessage message)
        {
            if (!(message.Message.Payload is RequestPartialTransactionPayload payload))
                return;

            // Don't process payloads whilst the federation wallet and cross chain store is syncing.
            if (!this.federationWalletManager.IsSyncedWithChain())
            {
                this.logger.Debug($"Federation payloads will only be processed once the federation wallet is synced; current height {this.federationWalletManager.WalletTipHeight}");
                return;
            }

            // Is a consolidation request.
            if (payload.DepositId == RequestPartialTransactionPayload.ConsolidationDepositId)
            {
                this.logger.Debug("Received request to sign consolidation transaction.");
                await this.HandleConsolidationTransactionRequestAsync(peer, payload);
                return;
            }

            this.logger.Debug("{0} with deposit Id '{1}' received from '{2}':'{3}'.", nameof(RequestPartialTransactionPayload), payload.DepositId, peer.PeerEndPoint.Address, peer.RemoteSocketAddress);

            ICrossChainTransfer[] transfer = await this.crossChainTransferStore.GetAsync(new[] { payload.DepositId }).ConfigureAwait(false);

            // This could be null if the store was unable to sync with the federation 
            // wallet manager. It is possible that the federation wallet's tip is not 
            // on chain and as such the store was not able to sync.
            if (transfer == null)
            {
                this.logger.Debug("{0}: Unable to retrieve transfers for deposit {1} at this time, the store is not synced.", nameof(this.OnMessageReceivedAsync), payload.DepositId);
                return;
            }

            if (transfer[0] == null)
            {
                this.logger.Debug("{0}: Deposit {1} does not exist.", nameof(this.OnMessageReceivedAsync), payload.DepositId);
                return;
            }

            if (transfer[0].Status != CrossChainTransferStatus.Partial)
            {
                this.logger.Debug("{0}: Deposit {1} is {2}.", nameof(this.OnMessageReceivedAsync), payload.DepositId, transfer[0].Status);
                return;
            }

            if (transfer[0].PartialTransaction == null)
            {
                this.logger.Debug("{0}: Deposit {1}, PartialTransaction not found.", nameof(this.OnMessageReceivedAsync), payload.DepositId);
                return;
            }

            uint256 oldHash = transfer[0].PartialTransaction.GetHash();

            Transaction signedTransaction = this.crossChainTransferStore.MergeTransactionSignatures(payload.DepositId, new[] { payload.PartialTransaction });

            if (signedTransaction == null)
            {
                this.logger.Debug("{0}: Deposit {1}, signedTransaction not found.", nameof(this.OnMessageReceivedAsync), payload.DepositId);
                return;
            }

            if (oldHash != signedTransaction.GetHash())
            {
                this.logger.Debug("Signed transaction (deposit={0}) to produce {1} from {2}.", payload.DepositId, signedTransaction.GetHash(), oldHash);

                // Respond back to the peer that requested a signature.
                await this.BroadcastAsync(payload.AddPartial(signedTransaction)).ConfigureAwait(false);
            }
            else
            {
                this.logger.Debug("The old and signed hash matches '{0}'.", oldHash);
            }
        }

        private async Task HandleConsolidationTransactionRequestAsync(INetworkPeer peer, RequestPartialTransactionPayload payload)
        {
            ConsolidationSignatureResult result = this.inputConsolidator.CombineSignatures(payload.PartialTransaction);

            if (result.Signed)
            {
                this.logger.Debug("Signed consolidating transaction to produce {0} from {1}", result.TransactionResult.GetHash(), payload.PartialTransaction.GetHash());
                await this.BroadcastAsync(payload.AddPartial(result.TransactionResult)).ConfigureAwait(false);
            }
        }
    }
}
