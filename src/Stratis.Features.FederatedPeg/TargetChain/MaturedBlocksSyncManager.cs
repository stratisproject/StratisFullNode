using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using NBitcoin;
using NLog;
using Stratis.Bitcoin.AsyncWork;
using Stratis.Bitcoin.Features.ExternalApi;
using Stratis.Bitcoin.Features.PoA;
using Stratis.Bitcoin.Interfaces;
using Stratis.Bitcoin.Utilities;
using Stratis.Features.FederatedPeg.Controllers;
using Stratis.Features.FederatedPeg.Conversion;
using Stratis.Features.FederatedPeg.Coordination;
using Stratis.Features.FederatedPeg.Interfaces;
using Stratis.Features.FederatedPeg.Models;
using Stratis.Features.FederatedPeg.SourceChain;
using Stratis.Features.FederatedPeg.Wallet;

namespace Stratis.Features.FederatedPeg.TargetChain
{
    /// <summary>
    /// Handles block syncing between gateways on 2 chains. This node will request
    /// blocks from another chain to look for cross chain deposit transactions.
    /// </summary>
    /// <remarks>Processes matured block deposits from the cirrus chain and creates instances of <see cref="ConversionRequest"/> which are
    /// saved to <see cref="IConversionRequestRepository"/>.</remarks>
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
        private readonly IFederatedPegSettings federatedPegSettings;
        private readonly IFederationWalletManager federationWalletManager;
        private readonly IInitialBlockDownloadState initialBlockDownloadState;
        private readonly ILogger logger;
        private readonly INodeLifetime nodeLifetime;
        private readonly IConversionRequestRepository conversionRequestRepository;
        private readonly ChainIndexer chainIndexer;
        private readonly IExternalApiPoller externalApiPoller;
        private readonly IConversionRequestFeeService conversionRequestFeeService;
        private readonly Network network;
        private readonly IFederationManager federationManager;

        private IAsyncLoop requestDepositsTask;

        /// <summary>When we are fully synced we stop asking for more blocks for this amount of time.</summary>
        private const int RefreshDelaySeconds = 10;

        /// <summary>Delay between initialization and first request to other node.</summary>
        /// <remarks>Needed to give other node some time to start before bombing it with requests.</remarks>
        private const int InitializationDelaySeconds = 10;

        public MaturedBlocksSyncManager(
            IAsyncProvider asyncProvider,
            ICrossChainTransferStore crossChainTransferStore,
            IFederationGatewayClient federationGatewayClient,
            IFederationWalletManager federationWalletManager,
            IInitialBlockDownloadState initialBlockDownloadState,
            INodeLifetime nodeLifetime,
            IConversionRequestRepository conversionRequestRepository,
            ChainIndexer chainIndexer,
            Network network,
            IFederatedPegSettings federatedPegSettings,
            IFederationManager federationManager = null,
            IExternalApiPoller externalApiPoller = null,
            IConversionRequestFeeService conversionRequestFeeService = null)
        {
            this.asyncProvider = asyncProvider;
            this.chainIndexer = chainIndexer;
            this.conversionRequestRepository = conversionRequestRepository;
            this.crossChainTransferStore = crossChainTransferStore;
            this.federationGatewayClient = federationGatewayClient;
            this.federatedPegSettings = federatedPegSettings;
            this.federationWalletManager = federationWalletManager;
            this.initialBlockDownloadState = initialBlockDownloadState;
            this.nodeLifetime = nodeLifetime;
            this.conversionRequestRepository = conversionRequestRepository;
            this.chainIndexer = chainIndexer;
            this.externalApiPoller = externalApiPoller;
            this.conversionRequestFeeService = conversionRequestFeeService;
            this.network = network;
            this.federationManager = federationManager;

            this.logger = LogManager.GetCurrentClassLogger();
        }

        /// <inheritdoc />
        public async Task StartAsync()
        {
            // Initialization delay; give the counter chain node some time to start it's API service.
            await Task.Delay(TimeSpan.FromSeconds(InitializationDelaySeconds), this.nodeLifetime.ApplicationStopping).ConfigureAwait(false);

            this.requestDepositsTask = this.asyncProvider.CreateAndRunAsyncLoop($"{nameof(MaturedBlocksSyncManager)}.{nameof(this.requestDepositsTask)}", async token =>
            {
                bool delayRequired = await this.SyncDepositsAsync().ConfigureAwait(false);
                if (delayRequired)
                {
                    // Since we are synced or had a problem syncing there is no need to ask for more blocks right away.
                    // Therefore awaiting for a delay during which new block might be accepted on the alternative chain
                    // or alt chain node might be started.
                    await Task.Delay(TimeSpan.FromSeconds(RefreshDelaySeconds), this.nodeLifetime.ApplicationStopping).ConfigureAwait(false);
                }
            },
            this.nodeLifetime.ApplicationStopping,
            repeatEvery: TimeSpan.FromSeconds(RefreshDelaySeconds));
        }

        /// <summary>Asks for blocks from another gateway node and then processes them.</summary>
        /// <returns><c>true</c> if delay between next time we should ask for blocks is required; <c>false</c> otherwise.</returns>
        protected async Task<bool> SyncDepositsAsync()
        {
            // First ensure that the federation wallet is active.
            if (!this.federationWalletManager.IsFederationWalletActive())
            {
                this.logger.Info("The CCTS will start processing deposits once the federation wallet has been activated.");
                return true;
            }

            // Then ensure that the node is out of IBD.
            if (this.initialBlockDownloadState.IsInitialBlockDownload())
            {
                this.logger.Info("The CCTS will start processing deposits once the node is out of IBD.");
                return true;
            }

            // Then ensure that the federation wallet is synced with the chain.
            if (!this.federationWalletManager.IsSyncedWithChain())
            {
                this.logger.Info($"The CCTS will start processing deposits once the federation wallet is synced with the chain; height {this.federationWalletManager.WalletTipHeight}");
                return true;
            }

            this.logger.Info($"Requesting deposits from counterchain node.");

            SerializableResult<List<MaturedBlockDepositsModel>> matureBlockDeposits = await this.federationGatewayClient.GetMaturedBlockDepositsAsync(this.crossChainTransferStore.NextMatureDepositHeight, this.nodeLifetime.ApplicationStopping).ConfigureAwait(false);

            if (matureBlockDeposits == null)
            {
                this.logger.Debug("Failed to fetch normal deposits from counter chain node; {0} didn't respond.", this.federationGatewayClient.EndpointUrl);
                return true;
            }

            if (matureBlockDeposits.Value == null)
            {
                this.logger.Debug("Failed to fetch normal deposits from counter chain node; {0} didn't reply with any deposits; Message: {1}", this.federationGatewayClient.EndpointUrl, matureBlockDeposits.Message ?? "none");
                return true;
            }

            return await ProcessMatureBlockDepositsAsync(matureBlockDeposits).ConfigureAwait(false);
        }

        private async Task<bool> ProcessMatureBlockDepositsAsync(SerializableResult<List<MaturedBlockDepositsModel>> matureBlockDeposits)
        {
            // "Value"'s count will be 0 if we are using NewtonSoft's serializer, null if using .Net Core 3's serializer.
            if (matureBlockDeposits.Value.Count == 0)
            {
                this.logger.Debug("Considering ourselves fully synced since no blocks were received.");

                // If we've received nothing we assume we are at the tip and should flush.
                // Same mechanic as with syncing headers protocol.
                await this.crossChainTransferStore.SaveCurrentTipAsync().ConfigureAwait(false);

                return true;
            }

            this.logger.Info("Processing {0} matured blocks.", matureBlockDeposits.Value.Count);

            // Filter out conversion transactions & also log what we've received for diagnostic purposes.
            foreach (MaturedBlockDepositsModel maturedBlockDeposit in matureBlockDeposits.Value)
            {
                var tempDepositList = new List<IDeposit>();

                if (maturedBlockDeposit.Deposits.Count > 0)
                    this.logger.Debug("Matured deposit count for block {0} height {1}: {2}.", maturedBlockDeposit.BlockInfo.BlockHash, maturedBlockDeposit.BlockInfo.BlockHeight, maturedBlockDeposit.Deposits.Count);

                foreach (IDeposit potentialConversionTransaction in maturedBlockDeposit.Deposits)
                {
                    // If this is not a conversion transaction then add it immediately to the temporary list.
                    if (potentialConversionTransaction.RetrievalType != DepositRetrievalType.ConversionSmall &&
                        potentialConversionTransaction.RetrievalType != DepositRetrievalType.ConversionNormal &&
                        potentialConversionTransaction.RetrievalType != DepositRetrievalType.ConversionLarge)
                    {
                        tempDepositList.Add(potentialConversionTransaction);
                        continue;
                    }

                    if (this.federatedPegSettings.IsMainChain)
                    {
                        this.logger.Warn("Conversion transactions do not get actioned by the main chain.");
                        continue;
                    }

                    var interFluxV2MainChainActivationHeight = ((PoAConsensusOptions)this.network.Consensus.Options).InterFluxV2MainChainActivationHeight;
                    if (interFluxV2MainChainActivationHeight != 0 && maturedBlockDeposit.BlockInfo.BlockHeight < interFluxV2MainChainActivationHeight)
                    {
                        this.logger.Warn("Conversion transactions '{0}' will not be processed below the main chain activation height of {1}.", potentialConversionTransaction.Id, interFluxV2MainChainActivationHeight);
                        continue;
                    }

                    this.logger.Info("Conversion transaction '{0}' received.", potentialConversionTransaction.Id);

                    ChainedHeader applicableHeader = null;
                    bool conversionExists = false;
                    if (this.conversionRequestRepository.Get(potentialConversionTransaction.Id.ToString()) != null)
                    {
                        this.logger.Warn("Conversion transaction '{0}' already exists, ignoring.", potentialConversionTransaction.Id);
                        conversionExists = true;
                    }
                    else
                    {
                        // This should ony happen if the conversion does't exist yet.
                        if (!FindApplicableConversionRequestHeader(maturedBlockDeposit, potentialConversionTransaction, out applicableHeader))
                            continue;
                    }

                    InteropConversionRequestFee interopConversionRequestFee = await this.conversionRequestFeeService.AgreeFeeForConversionRequestAsync(potentialConversionTransaction.Id.ToString(), maturedBlockDeposit.BlockInfo.BlockHeight).ConfigureAwait(false);

                    // If a dynamic fee could not be determined, create a fallback fee.
                    if (interopConversionRequestFee == null ||
                        (interopConversionRequestFee != null && interopConversionRequestFee.State != InteropFeeState.AgreeanceConcluded))
                    {
                        interopConversionRequestFee.Amount = ConversionRequestFeeService.FallBackFee;
                        this.logger.Warn($"A dynamic fee for conversion request '{potentialConversionTransaction.Id}' could not be determined, using a fixed fee of {ConversionRequestFeeService.FallBackFee} STRAX.");
                    }

                    if (Money.Satoshis(interopConversionRequestFee.Amount) >= potentialConversionTransaction.Amount)
                    {
                        this.logger.Warn("Conversion transaction '{0}' is no longer large enough to cover the fee.", potentialConversionTransaction.Id);
                        continue;
                    }

                    // We insert the fee distribution as a deposit to be processed, albeit with a special address.
                    // Deposits with this address as their destination will be distributed between the multisig members.
                    // Note that it will be actioned immediately as a matured deposit.
                    this.logger.Info("Adding conversion fee distribution for transaction '{0}' to deposit list.", potentialConversionTransaction.Id);

                    // Instead of being a conversion deposit, the fee distribution is translated to its non-conversion equivalent.
                    DepositRetrievalType depositType = DepositRetrievalType.Small;

                    switch (potentialConversionTransaction.RetrievalType)
                    {
                        case DepositRetrievalType.ConversionSmall:
                            depositType = DepositRetrievalType.Small;
                            break;
                        case DepositRetrievalType.ConversionNormal:
                            depositType = DepositRetrievalType.Normal;
                            break;
                        case DepositRetrievalType.ConversionLarge:
                            depositType = DepositRetrievalType.Large;
                            break;
                    }

                    tempDepositList.Add(new Deposit(potentialConversionTransaction.Id,
                        depositType,
                        Money.Satoshis(interopConversionRequestFee.Amount),
                        this.network.ConversionTransactionFeeDistributionDummyAddress,
                        potentialConversionTransaction.TargetChain,
                        potentialConversionTransaction.BlockNumber,
                        potentialConversionTransaction.BlockHash));

                    if (!conversionExists)
                    {
                        this.logger.Debug("Adding conversion request for transaction '{0}' to repository.", potentialConversionTransaction.Id);

                        this.conversionRequestRepository.Save(new ConversionRequest()
                        {
                            RequestId = potentialConversionTransaction.Id.ToString(),
                            RequestType = ConversionRequestType.Mint,
                            Processed = false,
                            RequestStatus = ConversionRequestStatus.Unprocessed,
                            // We do NOT convert to wei here yet. That is done when the minting transaction is submitted on the Ethereum network.
                            Amount = (ulong)(potentialConversionTransaction.Amount - Money.Satoshis(interopConversionRequestFee.Amount)).Satoshi,
                            BlockHeight = applicableHeader.Height,
                            DestinationAddress = potentialConversionTransaction.TargetAddress,
                            DestinationChain = potentialConversionTransaction.TargetChain
                        });
                    }
                }

                maturedBlockDeposit.Deposits = tempDepositList.AsReadOnly();

                // Order all non-conversion deposit transactions in the block deterministically.
                maturedBlockDeposit.Deposits = maturedBlockDeposit.Deposits.OrderBy(x => x.Id, Comparer<uint256>.Create(DeterministicCoinOrdering.CompareUint256)).ToList();

                foreach (IDeposit deposit in maturedBlockDeposit.Deposits)
                {
                    this.logger.Debug("Deposit matured: {0}", deposit.ToString());
                }
            }

            // If we received a portion of blocks we can ask for a new portion without any delay.
            RecordLatestMatureDepositsResult result = await this.crossChainTransferStore.RecordLatestMatureDepositsAsync(matureBlockDeposits.Value).ConfigureAwait(false);
            return !result.MatureDepositRecorded;
        }

        /// <summary>
        /// Get the first block on this chain that has a timestamp after the deposit's block time on the counterchain.
        /// This is so that we can assign a block height that the deposit 'arrived' on the sidechain.
        /// TODO: This can probably be made more efficient than looping every time. 
        /// </summary>
        /// <param name="maturedBlockDeposit">The matured block deposit's block time to check against.</param>
        /// <param name="potentialConversionTransaction">The conversion transaction we are currently working with.</param>
        /// <param name="chainedHeader">The chained header to use.</param>
        /// <returns><c>true</c> if found.</returns>
        private bool FindApplicableConversionRequestHeader(MaturedBlockDepositsModel maturedBlockDeposit, IDeposit potentialConversionTransaction, out ChainedHeader chainedHeader)
        {
            chainedHeader = this.chainIndexer.Tip;

            bool found = false;

            this.logger.Debug($"Finding applicable header for deposit with block time '{maturedBlockDeposit.BlockInfo.BlockTime}'; chain tip '{this.chainIndexer.Tip}'.");

            while (true)
            {
                if (chainedHeader == this.chainIndexer.Genesis)
                    break;

                if (chainedHeader.Previous.Header.Time <= maturedBlockDeposit.BlockInfo.BlockTime)
                {
                    found = true;
                    break;
                }

                chainedHeader = chainedHeader.Previous;
            }

            if (!found)
                this.logger.Warn("Unable to determine timestamp for conversion transaction '{0}', ignoring.", potentialConversionTransaction.Id);

            this.logger.Debug($"Applicable header selected '{chainedHeader}'");

            return found;
        }

        /// <inheritdoc />
        public void Dispose()
        {
            this.requestDepositsTask?.Dispose();
        }
    }
}
