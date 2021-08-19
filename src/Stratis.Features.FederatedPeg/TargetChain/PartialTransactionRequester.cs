using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using NLog;
using Stratis.Bitcoin.AsyncWork;
using Stratis.Bitcoin.Interfaces;
using Stratis.Bitcoin.Utilities;
using Stratis.Features.FederatedPeg.InputConsolidation;
using Stratis.Features.FederatedPeg.Interfaces;
using Stratis.Features.FederatedPeg.Payloads;

namespace Stratis.Features.FederatedPeg.TargetChain
{
    /// <summary>
    /// Requests partial transactions from the peers and calls <see cref="ICrossChainTransferStore.MergeTransactionSignatures".
    /// </summary>
    public interface IPartialTransactionRequester
    {
        /// <summary>
        /// Starts the broadcasting of partial transaction requests.
        /// </summary>
        void Start();

        /// <summary>
        /// Stops the broadcasting of partial transaction requests.
        /// </summary>
        void Stop();
    }

    /// <inheritdoc />
    public class PartialTransactionRequester : IPartialTransactionRequester
    {
        /// <summary>
        /// How often to trigger the query for and broadcasting of partial transactions.
        /// </summary>
        private static readonly TimeSpan TimeBetweenQueries = TimeSpans.TenSeconds;

        private readonly ILogger logger;
        private readonly ICrossChainTransferStore crossChainTransferStore;
        private readonly IAsyncProvider asyncProvider;
        private readonly INodeLifetime nodeLifetime;
        private readonly IFederatedPegBroadcaster federatedPegBroadcaster;
        private readonly IInitialBlockDownloadState ibdState;
        private readonly IFederationWalletManager federationWalletManager;
        private readonly IInputConsolidator inputConsolidator;

        private IAsyncLoop asyncLoop;

        public PartialTransactionRequester(
            ICrossChainTransferStore crossChainTransferStore,
            IAsyncProvider asyncProvider,
            INodeLifetime nodeLifetime,
            IFederatedPegBroadcaster federatedPegBroadcaster,
            IInitialBlockDownloadState ibdState,
            IFederationWalletManager federationWalletManager,
            IInputConsolidator inputConsolidator)
        {
            this.logger = LogManager.GetCurrentClassLogger();
            this.crossChainTransferStore = crossChainTransferStore;
            this.asyncProvider = asyncProvider;
            this.nodeLifetime = nodeLifetime;
            this.ibdState = ibdState;
            this.federatedPegBroadcaster = federatedPegBroadcaster;
            this.federationWalletManager = federationWalletManager;
            this.inputConsolidator = inputConsolidator;
        }

        public async Task BroadcastPartialTransactionsAsync()
        {
            if (this.ibdState.IsInitialBlockDownload() || !this.federationWalletManager.IsFederationWalletActive())
            {
                this.logger.Info("Federation wallet isn't active or in IBD. Not attempting to request transaction signatures.");
                return;
            }

            // Broadcast the partial transaction with the earliest inputs.
            IEnumerable<ICrossChainTransfer> partialtransfers = this.crossChainTransferStore.GetTransfersByStatus(new[] { CrossChainTransferStatus.Partial }, true);

            this.logger.Info($"Requesting partial templates for {partialtransfers.Count()} transfers.");

            foreach (ICrossChainTransfer transfer in partialtransfers)
            {
                await this.federatedPegBroadcaster.BroadcastAsync(new RequestPartialTransactionPayload(transfer.DepositTransactionId).AddPartial(transfer.PartialTransaction)).ConfigureAwait(false);
                this.logger.Debug("Partial template requested for deposit Id '{0}'", transfer.DepositTransactionId);
            }

            // If we don't have any broadcastable transactions, check if we have any consolidating transactions to sign.
            if (!partialtransfers.Any())
            {
                List<ConsolidationTransaction> consolidationTransactions = this.inputConsolidator.ConsolidationTransactions;
                if (consolidationTransactions != null)
                {
                    // Only take one at a time. These guys are big.
                    ConsolidationTransaction toSign = consolidationTransactions.FirstOrDefault(x => x.Status == ConsolidationTransactionStatus.Partial);

                    if (toSign != null)
                    {
                        await this.federatedPegBroadcaster.BroadcastAsync(new RequestPartialTransactionPayload(RequestPartialTransactionPayload.ConsolidationDepositId).AddPartial(toSign.PartialTransaction)).ConfigureAwait(false);
                        this.logger.Debug("Partial consolidating transaction requested for '{0}'.", toSign.PartialTransaction.GetHash());
                    }
                }
            }
        }

        /// <inheritdoc />
        public void Start()
        {
            this.asyncLoop = this.asyncProvider.CreateAndRunAsyncLoop(nameof(PartialTransactionRequester), async token =>
            {
                await this.BroadcastPartialTransactionsAsync().ConfigureAwait(false);
            },
            this.nodeLifetime.ApplicationStopping,
            TimeBetweenQueries);
        }

        /// <inheritdoc />
        public void Stop()
        {
            if (this.asyncLoop != null)
            {
                this.asyncLoop.Dispose();
                this.asyncLoop = null;
            }
        }
    }
}
