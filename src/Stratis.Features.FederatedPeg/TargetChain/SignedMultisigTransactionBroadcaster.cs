using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using Stratis.Bitcoin.AsyncWork;
using Stratis.Bitcoin.Configuration.Logging;
using Stratis.Bitcoin.Features.MemoryPool;
using Stratis.Bitcoin.Features.Wallet.Broadcasting;
using Stratis.Bitcoin.Features.Wallet.Interfaces;
using Stratis.Bitcoin.Interfaces;
using Stratis.Bitcoin.Utilities;
using Stratis.Features.FederatedPeg.Interfaces;

namespace Stratis.Features.FederatedPeg.TargetChain
{
    /// <summary>
    /// This component is responsible retrieving signed multisig transactions (from <see cref="ICrossChainTransferStore"/>)
    /// and broadcasting them into the network.
    /// </summary>
    public interface ISignedMultisigTransactionBroadcaster : IDisposable
    {
        /// <summary>
        /// Enables the node operator to try and manually push fully signed transactions.
        /// </summary>
        /// <returns>The asynchronous task returning <see cref="SignedMultisigTransactionBroadcastResult"/>.</returns>
        Task<SignedMultisigTransactionBroadcastResult> BroadcastFullySignedTransfersAsync();

        /// <summary>
        /// Starts the broadcasting of fully signed transactions every N seconds.
        /// </summary>
        void Start();
    }

    public class SignedMultisigTransactionBroadcaster : ISignedMultisigTransactionBroadcaster
    {
        private readonly IAsyncProvider asyncProvider;
        private readonly IBroadcasterManager broadcasterManager;
        private readonly ICrossChainTransferStore crossChainTransferStore;
        private readonly ILogger logger;
        private readonly MempoolManager mempoolManager;
        private readonly INodeLifetime nodeLifetime;
        private readonly IInitialBlockDownloadState ibdState;
        private readonly IFederationWalletManager federationWalletManager;

        private IAsyncLoop broadcastFullySignedTransfersTask;

        private const int RefreshDelaySeconds = 10;

        public SignedMultisigTransactionBroadcaster(
            IAsyncProvider asyncProvider,
            MempoolManager mempoolManager,
            IBroadcasterManager broadcasterManager,
            IInitialBlockDownloadState ibdState,
            IFederationWalletManager federationWalletManager,
            INodeLifetime nodeLifetime,
            ICrossChainTransferStore crossChainTransferStore = null)
        {
            this.asyncProvider = asyncProvider;
            this.broadcasterManager = Guard.NotNull(broadcasterManager, nameof(broadcasterManager));
            this.crossChainTransferStore = crossChainTransferStore;
            this.federationWalletManager = Guard.NotNull(federationWalletManager, nameof(federationWalletManager));
            this.mempoolManager = Guard.NotNull(mempoolManager, nameof(mempoolManager));
            this.ibdState = Guard.NotNull(ibdState, nameof(ibdState));
            this.nodeLifetime = nodeLifetime;

            this.logger = LogManager.GetCurrentClassLogger();
        }

        /// <inheritdoc />
        public void Start()
        {
            this.broadcastFullySignedTransfersTask = this.asyncProvider.CreateAndRunAsyncLoop($"{nameof(SignedMultisigTransactionBroadcaster)}.{nameof(this.broadcastFullySignedTransfersTask)}", async token =>
            {
                await this.BroadcastFullySignedTransfersAsync().ConfigureAwait(false);
            },
            this.nodeLifetime.ApplicationStopping,
            repeatEvery: TimeSpan.FromSeconds(RefreshDelaySeconds));
        }

        /// <inheritdoc />
        public async Task<SignedMultisigTransactionBroadcastResult> BroadcastFullySignedTransfersAsync()
        {
            if (this.ibdState.IsInitialBlockDownload() || !this.federationWalletManager.IsFederationWalletActive())
                return new SignedMultisigTransactionBroadcastResult() { Message = "The federation wallet isn't active or the node is in IBD." };

            ICrossChainTransfer[] fullySignedTransfers = this.crossChainTransferStore.GetTransfersByStatus(new[] { CrossChainTransferStatus.FullySigned });

            if (fullySignedTransfers.Length == 0)
            {
                this.logger.LogDebug("There are no fully signed transactions to broadcast.");
                return new SignedMultisigTransactionBroadcastResult() { Message = "There are no fully signed transactions to broadcast." };
            }

            var result = new SignedMultisigTransactionBroadcastResult();

            foreach (ICrossChainTransfer fullySignedTransfer in fullySignedTransfers)
            {
                result.Items.Add(await BroadcastFullySignedTransfersAsync(fullySignedTransfer).ConfigureAwait(false));
            }

            return result;
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

            this.logger.LogInformation("Broadcasting deposit '{0}', a signed multisig transaction '{1}' to the network.", crossChainTransfer.DepositTransactionId, crossChainTransfer.PartialTransaction.GetHash());

            await this.broadcasterManager.BroadcastTransactionAsync(crossChainTransfer.PartialTransaction).ConfigureAwait(false);

            // Check if transaction was added to a mempool.
            TransactionBroadcastEntry transactionBroadCastEntry = this.broadcasterManager.GetTransaction(crossChainTransfer.PartialTransaction.GetHash());
            if (transactionBroadCastEntry == null)
                return transferItem;

            // If there was no mempool error, then it safe to assume the transaction was broadcasted ok or already known.
            if (transactionBroadCastEntry.MempoolError == null)
                return transferItem;

            if (transactionBroadCastEntry.TransactionBroadcastState == TransactionBroadcastState.CantBroadcast && !CrossChainTransferStore.IsMempoolErrorRecoverable(transactionBroadCastEntry.MempoolError))
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
            this.broadcastFullySignedTransfersTask?.Dispose();
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
