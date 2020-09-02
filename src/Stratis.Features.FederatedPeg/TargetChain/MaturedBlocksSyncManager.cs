using System;
using System.Collections.Generic;
using System.Linq;
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
        Task StartAsync();
    }

    /// <inheritdoc cref="IMaturedBlocksSyncManager"/>
    public class MaturedBlocksSyncManager : IMaturedBlocksSyncManager
    {
        private readonly IAsyncProvider asyncProvider;
        private readonly ICrossChainTransferStore crossChainTransferStore;
        private readonly IFederationGatewayClient federationGatewayClient;
        private readonly ILogger logger;
        private readonly INodeLifetime nodeLifetime;

        private IAsyncLoop requestFasterDepositsTask;
        private IAsyncLoop requestNormalDeposits;

        /// <summary>The maximum amount of blocks to request at a time from alt chain.</summary>
        public const int MaxBlocksToRequest = 1000;

        /// <summary>When we are fully synced we stop asking for more blocks for this amount of time.</summary>
        private const int RefreshDelaySeconds = 10;

        /// <summary>Delay between initialization and first request to other node.</summary>
        /// <remarks>Needed to give other node some time to start before bombing it with requests.</remarks>
        private const int InitializationDelaySeconds = 10;

        public MaturedBlocksSyncManager(IAsyncProvider asyncProvider, ICrossChainTransferStore crossChainTransferStore, IFederationGatewayClient federationGatewayClient, ILoggerFactory loggerFactory, INodeLifetime nodeLifetime)
        {
            this.asyncProvider = asyncProvider;
            this.crossChainTransferStore = crossChainTransferStore;
            this.federationGatewayClient = federationGatewayClient;
            this.nodeLifetime = nodeLifetime;

            this.logger = loggerFactory.CreateLogger(this.GetType().FullName);
        }

        /// <inheritdoc />
        public async Task StartAsync()
        {
            // Initialization delay; give the counter chain node some time to start it's API service.
            await Task.Delay(InitializationDelaySeconds, this.nodeLifetime.ApplicationStopping).ConfigureAwait(false);

            this.requestNormalDeposits = this.asyncProvider.CreateAndRunAsyncLoop($"{nameof(MaturedBlocksSyncManager)}.{nameof(this.requestNormalDeposits)}", async token =>
            {
                bool delayRequired = await this.SyncNormalDepositsAsync().ConfigureAwait(false);
                if (delayRequired)
                {
                    // Since we are synced or had a problem syncing there is no need to ask for more blocks right away.
                    // Therefore awaiting for a delay during which new block might be accepted on the alternative chain
                    // or alt chain node might be started.
                    await Task.Delay(RefreshDelaySeconds, this.nodeLifetime.ApplicationStopping).ConfigureAwait(false);
                }
            },
            this.nodeLifetime.ApplicationStopping,
            repeatEvery: TimeSpan.FromSeconds(RefreshDelaySeconds));

            this.requestFasterDepositsTask = this.asyncProvider.CreateAndRunAsyncLoop($"{nameof(MaturedBlocksSyncManager)}.{nameof(this.federationGatewayClient)}", async token =>
            {
                bool delayRequired = await this.SyncFasterDepositsAsync().ConfigureAwait(false);
                if (delayRequired)
                {
                    // Since we are synced or had a problem syncing there is no need to ask for more blocks right away.
                    // Therefore awaiting for a delay during which new block might be accepted on the alternative chain
                    // or alt chain node might be started.
                    await Task.Delay(RefreshDelaySeconds, this.nodeLifetime.ApplicationStopping).ConfigureAwait(false);
                }
            },
            this.nodeLifetime.ApplicationStopping,
            repeatEvery: TimeSpan.FromSeconds(RefreshDelaySeconds));
        }

        /// <summary>Asks for blocks from another gateway node and then processes them.</summary>
        /// <returns><c>true</c> if delay between next time we should ask for blocks is required; <c>false</c> otherwise.</returns>
        protected async Task<bool> SyncNormalDepositsAsync()
        {
            SerializableResult<List<MaturedBlockDepositsModel>> model = await this.federationGatewayClient.GetMaturedBlockDepositsAsync(this.crossChainTransferStore.NextMatureDepositHeight, this.nodeLifetime.ApplicationStopping).ConfigureAwait(false);

            if (model == null)
            {
                this.logger.LogDebug("Failed to fetch normal deposits from counter chain node; {0} didn't respond.", this.federationGatewayClient.EndpointUrl);
                return true;
            }

            if (model.Value == null)
            {
                this.logger.LogDebug("Failed to fetch normal deposits from counter chain node; {0} didn't reply with any deposits; Message: {1}", this.federationGatewayClient.EndpointUrl, model.Message ?? "none");
                return true;
            }

            return await ProcessMatureBlockDepositsAsync(model);
        }

        /// <summary>Ask the counter chain node for blocks that contain faster deposits.</summary>
        /// <returns><c>true</c> if delay between next time we should ask for blocks is required; <c>false</c> otherwise.</returns>
        protected async Task<bool> SyncFasterDepositsAsync()
        {
            SerializableResult<List<MaturedBlockDepositsModel>> model = await this.federationGatewayClient.GetFasterMaturedBlockDepositsAsync(this.crossChainTransferStore.NextMatureDepositHeight, this.nodeLifetime.ApplicationStopping).ConfigureAwait(false);

            if (model == null)
            {
                this.logger.LogDebug("Failed to fetch faster deposits from counter chain node; {0} didn't respond.", this.federationGatewayClient.EndpointUrl);
                return true;
            }

            if (model.Value == null)
            {
                this.logger.LogDebug("Failed to fetch faster deposits from counter chain node; {0} didn't reply with any deposits; Message: {1}", this.federationGatewayClient.EndpointUrl, model.Message ?? "none");
                return true;
            }

            return await ProcessMatureBlockDepositsAsync(model);
        }

        private async Task<bool> ProcessMatureBlockDepositsAsync(SerializableResult<List<MaturedBlockDepositsModel>> matureBlockDepositsResult)
        {
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
            this.requestNormalDeposits?.Dispose();
            this.requestFasterDepositsTask?.Dispose();
        }
    }
}