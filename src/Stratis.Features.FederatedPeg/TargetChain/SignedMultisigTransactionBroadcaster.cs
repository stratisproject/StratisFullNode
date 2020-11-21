using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using Stratis.Bitcoin.EventBus;
using Stratis.Bitcoin.Features.MemoryPool;
using Stratis.Bitcoin.Features.Wallet.Broadcasting;
using Stratis.Bitcoin.Features.Wallet.Interfaces;
using Stratis.Bitcoin.Interfaces;
using Stratis.Bitcoin.Signals;
using Stratis.Bitcoin.Utilities;
using Stratis.Features.FederatedPeg.Events;
using Stratis.Features.FederatedPeg.Interfaces;

namespace Stratis.Features.FederatedPeg.TargetChain
{
    /// <summary>
    /// This component is responsible retrieving signed multisig transactions (from <see cref="ICrossChainTransferStore"/>)
    /// and broadcasting them into the network.
    /// </summary>
    public interface ISignedMultisigTransactionBroadcaster
    {
        /// <summary>
        /// Enables the node operator to try and manually push fully signed transactions.
        /// </summary>
        Task<SignedMultisigTransactionBroadcastResult> BroadcastFullySignedTransfersAsync();

        /// <summary>
        /// Starts the broadcasting of fully signed transactions every N seconds.
        /// </summary>
        void Start();

        /// <summary>
        /// Stops the broadcasting of fully signed transactions.
        /// </summary>
        void Stop();
    }

    public class SignedMultisigTransactionBroadcaster : ISignedMultisigTransactionBroadcaster, IDisposable
    {
        private readonly ILogger logger;
        private readonly MempoolManager mempoolManager;
        private readonly IBroadcasterManager broadcasterManager;
        private readonly ISignals signals;
        private readonly ICrossChainTransferStore crossChainTransferStore;

        private readonly IInitialBlockDownloadState ibdState;
        private readonly IFederationWalletManager federationWalletManager;
        private SubscriptionToken onCrossChainTransactionFullySignedSubscription;

        public SignedMultisigTransactionBroadcaster(
            ILoggerFactory loggerFactory,
            MempoolManager mempoolManager,
            IBroadcasterManager broadcasterManager,
            IInitialBlockDownloadState ibdState,
            IFederationWalletManager federationWalletManager,
            ISignals signals,
            ICrossChainTransferStore crossChainTransferStore = null)
        {
            Guard.NotNull(loggerFactory, nameof(loggerFactory));
            this.mempoolManager = Guard.NotNull(mempoolManager, nameof(mempoolManager));
            this.broadcasterManager = Guard.NotNull(broadcasterManager, nameof(broadcasterManager));
            this.ibdState = Guard.NotNull(ibdState, nameof(ibdState));
            this.federationWalletManager = Guard.NotNull(federationWalletManager, nameof(federationWalletManager));
            this.signals = Guard.NotNull(signals, nameof(signals));
            this.crossChainTransferStore = crossChainTransferStore;

            this.logger = loggerFactory.CreateLogger(this.GetType().FullName);
        }

        /// <inheritdoc />
        public async Task<SignedMultisigTransactionBroadcastResult> BroadcastFullySignedTransfersAsync()
        {
            if (this.ibdState.IsInitialBlockDownload() || !this.federationWalletManager.IsFederationWalletActive())
            {
                this.logger.LogInformation("Federation wallet isn't active or the node is in IBD.");
                return new SignedMultisigTransactionBroadcastResult() { Message = "The federation wallet isn't active or the node is in IBD." };
            }

            ICrossChainTransfer[] fullySignedTransfers = this.crossChainTransferStore.GetTransfersByStatus(new[] { CrossChainTransferStatus.FullySigned });

            if (fullySignedTransfers.Length == 0)
            {
                this.logger.LogInformation("There are no fully signed transactions to broadcast.");
                return new SignedMultisigTransactionBroadcastResult() { Message = "There are no fully signed transactions to broadcast." };
            }

            var result = new SignedMultisigTransactionBroadcastResult();

            foreach (ICrossChainTransfer fullySignedTransfer in fullySignedTransfers)
            {
                result.Items.Add(await BroadcastFullySignedTransfersAsync(fullySignedTransfer));
            }

            return result;
        }

        /// <inheritdoc />
        public void Start()
        {
            this.onCrossChainTransactionFullySignedSubscription = this.signals.Subscribe<CrossChainTransferTransactionFullySigned>(async (tx) => await this.OnCrossChainTransactionFullySignedAsync(tx).ConfigureAwait(false));
        }

        /// <inheritdoc />
        public void Stop()
        {
            if (this.onCrossChainTransactionFullySignedSubscription != null)
            {
                this.signals.Unsubscribe(this.onCrossChainTransactionFullySignedSubscription);
            }
        }

        private async Task OnCrossChainTransactionFullySignedAsync(CrossChainTransferTransactionFullySigned @event)
        {
            if (this.ibdState.IsInitialBlockDownload() || !this.federationWalletManager.IsFederationWalletActive())
            {
                this.logger.LogInformation("Federation wallet isn't active or the node is IBD.");
                return;
            }

            await BroadcastFullySignedTransfersAsync(@event.Transfer);
        }

        private async Task<SignedMultisigTransactionBroadcastResultItem> BroadcastFullySignedTransfersAsync(ICrossChainTransfer crossChainTransfer)
        {
            var transferItem = new SignedMultisigTransactionBroadcastResultItem()
            {
                DepositId = crossChainTransfer.DepositTransactionId?.ToString(),
                TransactionId = crossChainTransfer.PartialTransaction.GetHash().ToString(),
            };

            TxMempoolInfo txMempoolInfo = await this.mempoolManager.InfoAsync(crossChainTransfer.PartialTransaction.GetHash()).ConfigureAwait(false);
            if (txMempoolInfo != null)
            {
                this.logger.LogInformation("Deposit '{0}' already in the mempool.", crossChainTransfer.DepositTransactionId);
                transferItem.ItemMessage = $"Deposit '{crossChainTransfer.DepositTransactionId}' already in the mempool.";
                return transferItem;
            }

            this.logger.LogInformation("Broadcasting deposit '{0}', a signed multisig transaction '{1'} to the network.", crossChainTransfer.DepositTransactionId, crossChainTransfer.PartialTransaction.GetHash());

            await this.broadcasterManager.BroadcastTransactionAsync(crossChainTransfer.PartialTransaction).ConfigureAwait(false);

            // Check if transaction was added to a mempool.
            TransactionBroadcastEntry transactionBroadCastEntry = this.broadcasterManager.GetTransaction(crossChainTransfer.PartialTransaction.GetHash());
            if (transactionBroadCastEntry?.TransactionBroadcastState == TransactionBroadcastState.CantBroadcast && !CrossChainTransferStore.IsMempoolErrorRecoverable(transactionBroadCastEntry.MempoolError))
            {
                this.logger.LogWarning("Deposit '{0}' rejected: '{1}'.", crossChainTransfer.DepositTransactionId, transactionBroadCastEntry.ErrorMessage);
                this.crossChainTransferStore.RejectTransfer(crossChainTransfer);
                transferItem.ItemMessage = $"Deposit '{crossChainTransfer.DepositTransactionId}' rejected: '{transactionBroadCastEntry.ErrorMessage}'.";
                return transferItem;
            }

            return transferItem;
        }

        public void Dispose()
        {
            this.Stop();
        }
    }

    public sealed class SignedMultisigTransactionBroadcastResult
    {
        public SignedMultisigTransactionBroadcastResult()
        {
            this.Items = new List<SignedMultisigTransactionBroadcastResultItem>();
        }

        [JsonProperty("message")]
        public string Message { get; set; }

        [JsonProperty("items")]
        public List<SignedMultisigTransactionBroadcastResultItem> Items { get; set; }
    }

    public sealed class SignedMultisigTransactionBroadcastResultItem
    {
        [JsonProperty("depositId")]
        public string DepositId { get; set; }

        [JsonProperty("message")]
        public string ItemMessage { get; set; }

        [JsonProperty("transactionId")]
        public string TransactionId { get; set; }
    }
}
