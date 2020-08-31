using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using NBitcoin;
using Stratis.Bitcoin.AsyncWork;
using Stratis.Bitcoin.Utilities;
using Stratis.Features.FederatedPeg.Controllers;
using Stratis.Features.FederatedPeg.Interfaces;
using Stratis.Features.FederatedPeg.Models;
using Stratis.Features.FederatedPeg.Wallet;

namespace Stratis.Features.FederatedPeg.TargetChain
{
    /// <summary>
    /// Handles block syncing between gateways on 2 chains. This node will request
    /// blocks from another chain to look for cross chain deposit transactions.
    /// </summary>
    public interface IMaturedBlocksSyncManager : IDisposable
    {
        /// <summary>Starts requesting blocks from another chain.</summary>
        void Start();
    }

    /// <inheritdoc cref="IMaturedBlocksSyncManager"/>
    public class MaturedBlocksSyncManager : IMaturedBlocksSyncManager
    {
        private readonly IAsyncProvider asyncProvider;
        private readonly CancellationTokenSource cancellationTokenSource;
        private readonly ICrossChainTransferStore crossChainTransferStore;
        private readonly IFederationGatewayClient federationGatewayClient;
        private readonly ILogger logger;

        private Task blockRequestingTask;

        /// <summary>The maximum amount of blocks to request at a time from alt chain.</summary>
        public const int MaxBlocksToRequest = 1000;

        /// <summary>When we are fully synced we stop asking for more blocks for this amount of time.</summary>
        private const int RefreshDelaySeconds = 10;

        /// <summary>Delay between initialization and first request to other node.</summary>
        /// <remarks>Needed to give other node some time to start before bombing it with requests.</remarks>
        private const int InitializationDelaySeconds = 10;

        public MaturedBlocksSyncManager(ICrossChainTransferStore crossChainTransferStore, IFederationGatewayClient federationGatewayClient, ILoggerFactory loggerFactory, IAsyncProvider asyncProvider)
        {
            this.asyncProvider = asyncProvider;
            this.cancellationTokenSource = new CancellationTokenSource();
            this.crossChainTransferStore = crossChainTransferStore;
            this.federationGatewayClient = federationGatewayClient;

            this.logger = loggerFactory.CreateLogger(this.GetType().FullName);
        }

        /// <inheritdoc />
        public void Start()
        {
            this.blockRequestingTask = RequestMaturedBlocksContinuouslyAsync();
            this.asyncProvider.RegisterTask($"{nameof(MaturedBlocksSyncManager)}.{nameof(this.blockRequestingTask)}", this.blockRequestingTask);
        }

        /// <summary>Continuously requests matured blocks from the counter chain.</summary>
        private async Task RequestMaturedBlocksContinuouslyAsync()
        {
            try
            {
                // Initialization delay; give the counter chain node some time to start it's API service.
                await Task.Delay(InitializationDelaySeconds, this.cancellationTokenSource.Token).ConfigureAwait(false);

                while (!this.cancellationTokenSource.IsCancellationRequested)
                {
                    bool delayRequired = await this.SyncBatchOfBlocksAsync(this.cancellationTokenSource.Token).ConfigureAwait(false);

                    if (delayRequired)
                    {
                        // Since we are synced or had a problem syncing there is no need to ask for more blocks right away.
                        // Therefore awaiting for a delay during which new block might be accepted on the alternative chain
                        // or alt chain node might be started.
                        await Task.Delay(RefreshDelaySeconds, this.cancellationTokenSource.Token).ConfigureAwait(false);
                    }
                }
            }
            catch (OperationCanceledException)
            {
                this.logger.LogTrace("(-)[CANCELLED]");
            }
        }

        /// <summary>Asks for blocks from another gateway node and then processes them.</summary>
        /// <returns><c>true</c> if delay between next time we should ask for blocks is required; <c>false</c> otherwise.</returns>
        /// <exception cref="OperationCanceledException">Thrown when <paramref name="cancellationToken"/> is cancelled.</exception>
        protected async Task<bool> SyncBatchOfBlocksAsync(CancellationToken cancellationToken = default)
        {
            // First retrieve faster deposits.
            // TODO We can't look at CCTS. NextMatureDepositHeight for faster deposits.
            SerializableResult<List<MaturedBlockDepositsModel>> fasterDeposits = await this.federationGatewayClient.GetFasterMaturedBlockDepositsAsync(this.crossChainTransferStore.NextMatureDepositHeight).ConfigureAwait(false);

            // Then normal deposits.
            if (fasterDeposits.IsSuccess)
            {
                SerializableResult<List<MaturedBlockDepositsModel>> model = await this.federationGatewayClient.GetMaturedBlockDepositsAsync(this.crossChainTransferStore.NextMatureDepositHeight, cancellationToken).ConfigureAwait(false);
                fasterDeposits.Value.AddRange(model.Value);
            }

            var delayRequired = await ProcessMatureBlockDepositsAsync(fasterDeposits);
            return delayRequired;
        }

        private async Task<bool> ProcessMatureBlockDepositsAsync(SerializableResult<List<MaturedBlockDepositsModel>> matureBlockDepositsResult)
        {
            if (matureBlockDepositsResult == null)
            {
                this.logger.LogDebug("Failed to fetch matured block deposits from counter chain node; {0} didn't respond.", this.federationGatewayClient.EndpointUrl);
                return true;
            }

            if (matureBlockDepositsResult.Value == null)
            {
                this.logger.LogDebug("Failed to fetch matured block deposits from counter chain node; {0} didn't reply with any deposits; Message: {1}", this.federationGatewayClient.EndpointUrl, matureBlockDepositsResult.Message ?? "none");
                return true;
            }

            bool delayRequired = true;

            // Log what we've received.
            foreach (MaturedBlockDepositsModel maturedBlockDeposit in matureBlockDepositsResult.Value)
            {
                // Order transactions in block deterministically
                maturedBlockDeposit.Deposits = maturedBlockDeposit.Deposits.OrderBy(x => x.Id, Comparer<uint256>.Create(DeterministicCoinOrdering.CompareUint256)).ToList();

                foreach (IDeposit deposit in maturedBlockDeposit.Deposits)
                {
                    this.logger.LogDebug("New deposit received BlockNumber={0}, TargetAddress='{1}', depositId='{2}', Amount='{3}'.", deposit.BlockNumber, deposit.TargetAddress, deposit.Id, deposit.Amount);
                }
            }

            if (matureBlockDepositsResult.Value.Count > 0)
            {
                RecordLatestMatureDepositsResult result = await this.crossChainTransferStore.RecordLatestMatureDepositsAsync(matureBlockDepositsResult.Value).ConfigureAwait(false);

                // If we received a portion of blocks we can ask for new portion without any delay.
                if (result.MatureDepositRecorded)
                    delayRequired = false;
            }
            else
            {
                this.logger.LogDebug("Considering ourselves fully synced since no blocks were received.");

                // If we've received nothing we assume we are at the tip and should flush.
                // Same mechanic as with syncing headers protocol.
                await this.crossChainTransferStore.SaveCurrentTipAsync().ConfigureAwait(false);
            }

            return delayRequired;
        }

        /// <inheritdoc />
        public void Dispose()
        {
            this.cancellationTokenSource.Cancel();
            this.blockRequestingTask?.GetAwaiter().GetResult();
        }
    }
}