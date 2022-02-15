using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading.Tasks;
using NBitcoin;
using NBitcoin.Crypto;
using Nethereum.RPC.Eth.DTOs;
using Nethereum.Util;
using Nethereum.Web3;
using NLog;
using Stratis.Bitcoin.AsyncWork;
using Stratis.Bitcoin.Configuration;
using Stratis.Bitcoin.Features.ExternalApi;
using Stratis.Bitcoin.Features.Interop.ETHClient;
using Stratis.Bitcoin.Features.Interop.Exceptions;
using Stratis.Bitcoin.Features.Interop.Payloads;
using Stratis.Bitcoin.Features.Interop.Settings;
using Stratis.Bitcoin.Features.PoA;
using Stratis.Bitcoin.Features.Wallet;
using Stratis.Bitcoin.Interfaces;
using Stratis.Bitcoin.Persistence;
using Stratis.Bitcoin.Utilities;
using Stratis.Features.Collateral.CounterChain;
using Stratis.Features.FederatedPeg.Conversion;
using Stratis.Features.FederatedPeg.Coordination;
using Stratis.Features.FederatedPeg.Distribution;
using Stratis.Features.FederatedPeg.Interfaces;
using Stratis.SmartContracts;
using Stratis.SmartContracts.CLR;

namespace Stratis.Bitcoin.Features.Interop
{
    public sealed class InteropPoller : IDisposable
    {
        private const string LastPolledBlockKey = "LastPolledBlock_{0}";

        /// <summary>
        /// We are giving a reorg window of 12 blocks here, so burns right at the tip won't be processed until they have 12 confirmations. 
        /// </summary>
        private const int DestinationChainReorgWindow = 12;

        /// <summary>
        /// If the last polled block for a given destination chain is more than this amount of blocks from the chain's tip,
        /// fast sync it up before the async loop poller takes over.
        /// </summary>
        private const int DestinationChainSyncToBuffer = 25;

        /// <summary>1x10^24 wei = 1 000 000 tokens</summary>
        public BigInteger ReserveBalanceTarget = BigInteger.Parse("1000000000000000000000000");

        /// <summary>The number of blocks deep a submission transaction needs to be before it should start getting confirmed by the non-originating nodes.</summary>
        public readonly BigInteger SubmissionConfirmationThreshold = 12;

        private readonly IAsyncProvider asyncProvider;
        private readonly ChainIndexer chainIndexer;
        private readonly Network counterChainNetwork;
        private readonly IConversionRequestCoordinationService conversionRequestCoordinationService;
        private readonly IConversionRequestRepository conversionRequestRepository;
        private readonly IExternalApiPoller externalApiPoller;
        private readonly IETHCompatibleClientProvider ethClientProvider;
        private readonly IFederationManager federationManager;
        private readonly IFederationHistory federationHistory;
        private readonly IFederatedPegBroadcaster federatedPegBroadcaster;
        private readonly InteropSettings interopSettings;
        private readonly IInitialBlockDownloadState initialBlockDownloadState;
        private readonly IKeyValueRepository keyValueRepository;
        private readonly ILogger logger;
        private readonly Network network;
        private readonly INodeLifetime nodeLifetime;
        private readonly ICirrusContractClient cirrusClient;

        private IAsyncLoop interopLoop;
        private IAsyncLoop conversionLoop;
        private IAsyncLoop conversionBurnTransferLoop;

        private readonly Dictionary<DestinationChain, BigInteger> lastPolledBlock;

        // Normally all the values of this dictionary will be -1. If they are anything else, the lastPolledBlock will
        // be reset to that value on the next async loop iteration.
        private readonly Dictionary<DestinationChain, BigInteger> overrideLastPolledBlock;

        /// <summary>Both the <see cref="conversionLoop"/> and <see cref="conversionBurnTransferLoop"/> need to access the repository, so access to it should be locked to ensure consistency.</summary>
        private readonly object repositoryLock;

        public InteropPoller(NodeSettings nodeSettings,
            InteropSettings interopSettings,
            IAsyncProvider asyncProvider,
            INodeLifetime nodeLifetime,
            ChainIndexer chainIndexer,
            IConversionRequestRepository conversionRequestRepository,
            IConversionRequestCoordinationService conversionRequestCoordinationService,
            CounterChainNetworkWrapper counterChainNetworkWrapper,
            IETHCompatibleClientProvider ethClientProvider,
            IExternalApiPoller externalApiPoller,
            IInitialBlockDownloadState initialBlockDownloadState,
            IFederationManager federationManager,
            IFederationHistory federationHistory,
            IFederatedPegBroadcaster federatedPegBroadcaster,
            IKeyValueRepository keyValueRepository,
            INodeStats nodeStats,
            ICirrusContractClient cirrusClient)
        {
            this.interopSettings = interopSettings;
            this.ethClientProvider = ethClientProvider;
            this.network = nodeSettings.Network;
            this.asyncProvider = asyncProvider;
            this.nodeLifetime = nodeLifetime;
            this.chainIndexer = chainIndexer;
            this.initialBlockDownloadState = initialBlockDownloadState;
            this.federationManager = federationManager;
            this.federationHistory = federationHistory;
            this.federatedPegBroadcaster = federatedPegBroadcaster;
            this.conversionRequestRepository = conversionRequestRepository;
            this.conversionRequestCoordinationService = conversionRequestCoordinationService;
            this.counterChainNetwork = counterChainNetworkWrapper.CounterChainNetwork;
            this.externalApiPoller = externalApiPoller;
            this.keyValueRepository = keyValueRepository;
            this.logger = LogManager.GetCurrentClassLogger();
            this.cirrusClient = cirrusClient;

            this.lastPolledBlock = new Dictionary<DestinationChain, BigInteger>();

            this.overrideLastPolledBlock = new Dictionary<DestinationChain, BigInteger>();

            this.repositoryLock = new object();

            nodeStats.RegisterStats(this.AddComponentStats, StatsType.Component, this.GetType().Name, 250);
        }

        /// <summary>
        /// Initializes the poller by starting the periodic loops that check for and process the conversion requests.
        /// </summary>
        public async Task InitializeAsync()
        {
            if (!this.ethClientProvider.GetAllSupportedChains().Any())
            {
                // There are no chains that are supported and enabled, exit.
                this.logger.Info("Interop disabled.");
                return;
            }

            if (!this.federationManager.IsFederationMember)
            {
                this.logger.Debug("Not a federation member.");
                return;
            }

            // Initialize the interop polling loop, to check for interop contract requests.
            this.interopLoop = this.asyncProvider.CreateAndRunAsyncLoop("PeriodicCheckInteropNodes", async (cancellation) =>
            {
                if (this.initialBlockDownloadState.IsInitialBlockDownload())
                    return;

                this.logger.Debug("Executing check interop nodes loop.");

                try
                {
                    await this.CheckInteropNodesAsync().ConfigureAwait(false);
                }
                catch (Exception e)
                {
                    this.logger.Warn("Exception raised when checking interop nodes. {0}", e);
                }
            },
            this.nodeLifetime.ApplicationStopping,
            repeatEvery: TimeSpans.TenSeconds,
            startAfter: TimeSpans.Second);

            // Initialize the conversion polling loop, to check for conversion requests.
            this.conversionLoop = this.asyncProvider.CreateAndRunAsyncLoop("PeriodicCheckInteropNodesForMintRequests", async (cancellation) =>
            {
                if (this.initialBlockDownloadState.IsInitialBlockDownload())
                    return;

                this.logger.Debug("Executing check for minting/conversion requests.");

                try
                {
                    await this.ProcessConversionRequestsAsync().ConfigureAwait(false);
                }
                catch (Exception e)
                {
                    this.logger.Warn($"Exception occurred checking for minting/conversion requests: {e}");
                }
            },
            this.nodeLifetime.ApplicationStopping,
            repeatEvery: TimeSpans.TenSeconds,
            startAfter: TimeSpans.Second);

            foreach (KeyValuePair<DestinationChain, IETHClient> supportedChain in this.ethClientProvider.GetAllSupportedChains())
            {
                await LoadLastPolledBlockForBurnAndTransferRequestsAsync(supportedChain.Key).ConfigureAwait(false);
            }

            // Call this explicitly for Cirrus as it does not fall into the ethClientProvider group.
            // TODO: This could be refactored in the future. 
            await LoadLastPolledBlockForBurnAndTransferRequestsAsync(DestinationChain.CIRRUS).ConfigureAwait(false);

            this.conversionBurnTransferLoop = this.asyncProvider.CreateAndRunAsyncLoop("PeriodicCheckForBurnsAndTransfers", async (cancellation) =>
            {
                if (this.initialBlockDownloadState.IsInitialBlockDownload())
                    return;

                if (this.interopSettings.GetSettings<CirrusInteropSettings>().CirrusWalletCredentials == null)
                {
                    this.logger.Warn("Cirrus interop wallet credentials not set, please call the initialize interflux endpoint first so that burns and transfers can be checked.");
                    return;
                }

                this.logger.Info("Executing check for any burns or transfers.");

                // In the event that the last polled block was set back a considerable distance from the tip, we need to first catch up faster.
                // If we are already in the acceptable range, the usual logic will apply.
                await EnsureLastPolledBlockIsSyncedWithChainAsync().ConfigureAwait(false);

                try
                {
                    foreach (KeyValuePair<DestinationChain, IETHClient> supportedChain in this.ethClientProvider.GetAllSupportedChains())
                    {
                        CheckForBlockHeightOverrides(supportedChain.Key);

                        BigInteger blockHeight = await supportedChain.Value.GetBlockHeightAsync().ConfigureAwait(false);

                        this.logger.Info($"Last polled block for burns and transfers: {this.lastPolledBlock[supportedChain.Key]}; current {supportedChain.Key} chain height: {blockHeight}");

                        if (this.lastPolledBlock[supportedChain.Key] < (blockHeight - DestinationChainReorgWindow))
                            await PollBlockForBurnsAndTransfersAsync(supportedChain, blockHeight).ConfigureAwait(false);
                    }

                    // The logic duplication is unfortunate, but Cirrus doesn't have an IEthClient so it has to be handled differently.
                    CheckForBlockHeightOverrides(DestinationChain.CIRRUS);

                    ConsensusTipModel cirrusTip = await this.cirrusClient.GetConsensusTipAsync().ConfigureAwait(false);

                    BigInteger cirrusBlockHeight = cirrusTip.TipHeight;

                    this.logger.Info($"Last polled Cirrus block for burns and transfers: {this.lastPolledBlock[DestinationChain.CIRRUS]}; current Cirrus chain height: {cirrusBlockHeight}");

                    if (this.lastPolledBlock[DestinationChain.CIRRUS] < (cirrusBlockHeight - DestinationChainReorgWindow))
                        await PollCirrusForBurnsAsync(cirrusBlockHeight).ConfigureAwait(false);
                }
                catch (Exception e)
                {
                    this.logger.Warn($"Exception raised when polling for conversion burn & transfer transactions. {e}");
                }
            },
            this.nodeLifetime.ApplicationStopping,
            repeatEvery: TimeSpans.TenSeconds,
            startAfter: TimeSpans.Second);
        }

        public void CheckForBlockHeightOverrides(DestinationChain chain)
        {
            if (!this.overrideLastPolledBlock.TryGetValue(chain, out BigInteger blockHeight))
                return;

            if (blockHeight == BigInteger.MinusOne)
                return;

            this.logger.Info($"Resetting scan height for burns and transfers on chain {chain} to {this.overrideLastPolledBlock[chain]}.");

            this.lastPolledBlock[chain] = this.overrideLastPolledBlock[chain];
            this.overrideLastPolledBlock[chain] = BigInteger.MinusOne;

            SaveLastPolledBlockForBurnsAndTransfers(chain);
        }

        public void ResetScanHeight(DestinationChain chain, int height)
        {
            this.overrideLastPolledBlock[chain] = height;
        }

        /// <summary>
        /// Loads the last polled block from the store.
        /// </summary>
        private async Task LoadLastPolledBlockForBurnAndTransferRequestsAsync(DestinationChain destinationChain)
        {
            var loaded = this.keyValueRepository.LoadValueJson<int>(string.Format(LastPolledBlockKey, destinationChain));

            // If this has never been loaded, set this to the current height of the applicable chain.
            if (loaded == 0)
            {
                if (destinationChain == DestinationChain.CIRRUS)
                {
                    ConsensusTipModel model = await this.cirrusClient.GetConsensusTipAsync().ConfigureAwait(false);
                    this.lastPolledBlock[DestinationChain.CIRRUS] = model.TipHeight;
                }
                else
                    this.lastPolledBlock[destinationChain] = await this.ethClientProvider.GetClientForChain(destinationChain).GetBlockHeightAsync().ConfigureAwait(false);
            }
            else
                this.lastPolledBlock[destinationChain] = loaded;

            this.overrideLastPolledBlock[destinationChain] = BigInteger.MinusOne;

            this.logger.Info($"Last polled block for burns and transfers on chain {destinationChain} set to {this.lastPolledBlock[destinationChain]}.");
        }

        private void SaveLastPolledBlockForBurnsAndTransfers(DestinationChain destinationChain)
        {
            this.keyValueRepository.SaveValueJson(string.Format(LastPolledBlockKey, destinationChain), this.lastPolledBlock[destinationChain]);
            this.logger.Info($"Last polled block for burns and transfers on chain {destinationChain} saved as {this.lastPolledBlock[destinationChain]}.");
        }

        /// <summary>
        /// If the last polled block is more than 50 blocks from the chain's tip, 
        /// then sync it up so that the async loop task can take over from a point closer
        /// to the tip.
        /// </summary>
        private async Task EnsureLastPolledBlockIsSyncedWithChainAsync()
        {
            foreach (KeyValuePair<DestinationChain, IETHClient> supportedChain in this.ethClientProvider.GetAllSupportedChains())
            {
                BigInteger blockHeight = await supportedChain.Value.GetBlockHeightAsync().ConfigureAwait(false);

                while (this.lastPolledBlock[supportedChain.Key] < (blockHeight - DestinationChainReorgWindow - DestinationChainSyncToBuffer))
                {
                    await PollBlockForBurnsAndTransfersAsync(supportedChain, blockHeight).ConfigureAwait(false);
                }
            }
        }

        private async Task PollCirrusForBurnsAsync(BigInteger blockHeight)
        {
            this.logger.Info("Polling Cirrus block at height {0} for burn transactions.", blockHeight);

            NBitcoin.Block block = await this.cirrusClient.GetBlockByHeightAsync((int)blockHeight).ConfigureAwait(false);
            if (block == null)
            {
                this.logger.Info($"Unable to retrieve block with height {blockHeight}.");

                // We shouldn't update the block height before returning because we might skip a block. 
                return;
            }

            // TODO: Need to build these sets across all supported chains
            HashSet<string> watchedSrc20Contracts = this.interopSettings.GetSettingsByChain(DestinationChain.ETH).WatchedErc20Contracts.Values.ToHashSet();
            HashSet<string> watchedSrc721Contracts = this.interopSettings.GetSettingsByChain(DestinationChain.ETH).WatchedErc721Contracts.Values.ToHashSet();

            var zeroAddressRaw = new uint160(Address.Zero.ToBytes());
            string zeroAddress = zeroAddressRaw.ToBase58Address(this.network);

            foreach (NBitcoin.Transaction transaction in block.Transactions.Where(t => t.IsSmartContractExecTransaction()))
            {
                try
                {
                    CirrusReceiptResponse receipt = await this.cirrusClient.GetReceiptAsync(transaction.GetHash().ToString()).ConfigureAwait(false);

                    // This is probably a normal Cirrus transfer (if null), or a failed contract call that should be ignored.
                    if (receipt == null || !receipt.Success)
                    {
                        this.logger.Debug($"Transaction {transaction.GetHash()} did not contain a receipt.");
                        continue;
                    }

                    // Filter out calls to contracts that we aren't monitoring.
                    if (!watchedSrc20Contracts.Contains(receipt.To) && !watchedSrc721Contracts.Contains(receipt.To))
                        continue;

                    this.logger.Info($"Found transaction {receipt.TransactionHash} from {receipt.From} with a receipt that affects watched contract {receipt.To}.");

                    // We don't easily know if this was an SRC20 or SRC721 burn, unless we use the watched hashsets as a lookup.
                    // In any case we have to validate all the fields in the relevant receipt logs.
                    foreach (CirrusLogResponse log in receipt.Logs)
                    {
                        TransferDetails src20burn = ExtractBurnFromBurnMetadataLog(log);

                        if (src20burn != null)
                        {
                            this.logger.Info($"Found a valid burn transaction with metadata: {src20burn.To}.");
                        }

                        // TODO: Awaiting an InterFluxNonFungibleToken contract that has a 'burn with metadata' method
                        //TransferDetails src721burn = ExtractBurnFromTransferLog(log, zeroAddress);

                        // TODO: Need to insert the burns into the conversion request repository & extend the InteropPoller state machine to handle Cirrus -> Ethereum transfer requests
                    }
                }
                catch (Exception e)
                {
                    this.logger.Error("Error processing Cirrus block {0} for burns: {1}", blockHeight, e);
                }
            }

            this.lastPolledBlock[DestinationChain.CIRRUS] += 1;

            SaveLastPolledBlockForBurnsAndTransfers(DestinationChain.CIRRUS);
        }

        /// <summary>
        /// SRC20 InterFluxStandardToken burns.
        /// </summary>
        /// <param name="log">The log data from a Cirrus smart contract receipt.</param>
        /// <returns>A <see cref="TransferDetails"/> instance if this was a valid SRC721 burn, else null.</returns>
        private TransferDetails ExtractBurnFromBurnMetadataLog(CirrusLogResponse log)
        {
            // We presume that anything emitting this event must be a burn and thus the 'To' address is neither included in the event nor validated here.
            if (log.Log.Event != "BurnMetadata")
                return null;

            if (!log.Log.Data.TryGetValue("from", out object from))
                return null;

            string fromAddress = (string)from;

            if (!log.Log.Data.TryGetValue("metadata", out object metadata))
                return null;

            // TODO: Check that this string is in an acceptable format? Maybe it should encode the destination chain as well
            string metadataString = (string)metadata;

            if (!log.Log.Data.TryGetValue("amount", out object amount))
                return null;

            string amountString = (string)amount;

            if (!int.TryParse(amountString, out int transferAmount))
                return null;

            var transfer = new TransferDetails()
            {
                ContractType = ContractType.ERC20,
                From = fromAddress,
                To = metadataString,
                TransferType = TransferType.Burn,
                Value = transferAmount
            };

            return transfer;
        }

        /// <summary>
        /// For SRC721 NonFungibleToken burns.
        /// </summary>
        /// <param name="log">The log data from a Cirrus smart contract receipt.</param>
        /// <param name="zeroAddress">The base58 representation of <see cref="Address.Zero"/>.</param>
        /// <returns>A <see cref="TransferDetails"/> instance if this was a valid SRC721 burn, else null.</returns>
        private TransferDetails ExtractBurnFromTransferLog(CirrusLogResponse log, string zeroAddress)
        {
            throw new NotImplementedException("This will not work yet, the NFT contract should instead be adapted to emit a BurnMetadata log as well");

            if (log.Log.Event != "TransferLog")
                return null;

            if (!log.Log.Data.TryGetValue("from", out object from))
                return null;

            string fromAddress = (string)from;

            if (!log.Log.Data.TryGetValue("to", out object to))
                return null;

            string toAddress = (string)to;

            // If it's not being transferred to the zero address it isn't a burn, and can thus be ignored.
            if (toAddress != zeroAddress)
                return null;

            if (!log.Log.Data.TryGetValue("tokenId", out object tokenId))
                return null;

            string tokenIdString = (string)tokenId;

            if (!int.TryParse(tokenIdString, out int tokenIdInt))
                return null;

            var transfer = new TransferDetails()
            {
                ContractType = ContractType.ERC721,
                From = fromAddress,
                To = toAddress,
                TransferType = TransferType.Burn,
                Value = tokenIdInt
            };

            return transfer;
        }

        private async Task PollBlockForBurnsAndTransfersAsync(KeyValuePair<DestinationChain, IETHClient> supportedChain, BigInteger blockHeight)
        {
            this.logger.Info("Polling {0} block at height {1} for burn or transfer transactions.", supportedChain.Key, blockHeight);

            BlockWithTransactions block = await supportedChain.Value.GetBlockAsync(this.lastPolledBlock[supportedChain.Key]).ConfigureAwait(false);

            // TODO: Move this check into the same method as the transfers to save iterating over the entire Ethereum block twice
            List<(string TransactionHash, BurnFunction Burn)> burns = await supportedChain.Value.GetWStraxBurnsFromBlockAsync(block).ConfigureAwait(false);

            foreach ((string TransactionHash, BurnFunction Burn) in burns)
            {
                this.ProcessWStraxBurn(block.BlockHash, TransactionHash, Burn);
            }

            if (this.interopSettings.GetSettingsByChain(supportedChain.Key).WatchedErc20Contracts.Any() || this.interopSettings.GetSettingsByChain(supportedChain.Key).WatchedErc721Contracts.Any())
            {
                List<(string TransactionHash, string TransferContractAddress, TransferDetails Transfer)> transfers = await supportedChain.Value.GetTransfersFromBlockAsync(block,
                    this.interopSettings.GetSettingsByChain(supportedChain.Key).WatchedErc20Contracts.Keys.ToHashSet(),
                    this.interopSettings.GetSettingsByChain(supportedChain.Key).WatchedErc721Contracts.Keys.ToHashSet()).ConfigureAwait(false);

                foreach ((string TransactionHash, string TransferContractAddress, TransferDetails Transfer) in transfers)
                {
                    await this.ProcessTransferAsync(block.BlockHash, TransactionHash, TransferContractAddress, Transfer, supportedChain).ConfigureAwait(false);
                }
            }

            this.lastPolledBlock[supportedChain.Key] += 1;

            SaveLastPolledBlockForBurnsAndTransfers(supportedChain.Key);
        }

        /// <summary>Retrieves the current chain heights of interop enabled chains via the RPC interface.</summary>
        private async Task CheckInteropNodesAsync()
        {
            foreach (KeyValuePair<DestinationChain, IETHClient> clientForChain in this.ethClientProvider.GetAllSupportedChains())
            {
                // Retrieves current chain height via the RPC interface to geth.
                try
                {
                    BigInteger blockHeight = await clientForChain.Value.GetBlockHeightAsync().ConfigureAwait(false);

                    // TODO Add back or refactor so that this is specific per chain (if applicable)
                    // BigInteger balance = await this.ETHClientBase.GetBalanceAsync(this.interopSettings.ETHAccount).ConfigureAwait(false);

                    this.logger.Info("Current {0} node block height is {1}.", clientForChain.Key, blockHeight);
                }
                catch (Exception e)
                {
                    this.logger.Error("Error checking {0} node status: {1}", clientForChain.Key, e);
                }
            }
        }

        /// <summary>
        /// Processes burn() contract calls made against the Wrapped Strax contract deployed on a specific chain.
        /// </summary>
        /// <param name="blockHash">The hash of the block the burn transaction appeared in.</param>
        /// <param name="transactionHash">The hash of the transaction that the burn method call appeared in.</param>
        /// <param name="burn">The metadata of the burn method call.</param>
        private void ProcessWStraxBurn(string blockHash, string transactionHash, BurnFunction burn)
        {
            this.logger.Info("Conversion burn transaction '{0}' received from polled block '{1}', sender {2}.", transactionHash, blockHash, burn.FromAddress);

            lock (this.repositoryLock)
            {
                if (this.conversionRequestRepository.Get(transactionHash) != null)
                {
                    this.logger.Info("Conversion burn transaction '{0}' already exists, ignoring.", transactionHash);

                    return;
                }
            }

            this.logger.Info("Conversion burn transaction '{0}' has value {1}.", transactionHash, burn.Amount);

            // Get the destination address recorded in the contract call itself. This has the benefit that subsequent burn calls from the same account providing different addresses will not interfere with this call.
            string destinationAddress = burn.StraxAddress;

            this.logger.Info("Conversion burn transaction '{0}' has destination address {1}.", transactionHash, destinationAddress);

            // Validate that it is a mainchain address here before bothering to add it to the repository.
            try
            {
                BitcoinAddress.Create(destinationAddress, this.counterChainNetwork);
            }
            catch (Exception)
            {
                this.logger.Warn("Error validating destination address '{0}' for transaction '{1}'.", destinationAddress, transactionHash);

                return;
            }

            // Schedule this transaction to be processed at the next block height that is divisible by 100. 
            // Thus will round up to the nearest 100 then add another another 100.
            // In this way, burns will always be scheduled at a predictable future time across the multisig.
            // This is because we cannot predict exactly when each node is polling the Ethereum chain for events.
            ulong blockHeight = (ulong)(this.chainIndexer.Tip.Height - (this.chainIndexer.Tip.Height % 50) + 100);

            if (blockHeight <= 0)
                blockHeight = 100;

            lock (this.repositoryLock)
            {
                this.conversionRequestRepository.Save(new ConversionRequest()
                {
                    RequestId = transactionHash,
                    RequestType = ConversionRequestType.Burn,
                    Processed = false,
                    RequestStatus = ConversionRequestStatus.Unprocessed,
                    Amount = this.ConvertWeiToSatoshi(burn.Amount),
                    BlockHeight = (int)blockHeight,
                    DestinationAddress = destinationAddress,
                    DestinationChain = DestinationChain.STRAX
                });
            }
        }

        /// <summary>
        /// Processes transfer() contract calls made against one of the ERC20 token contracts being monitored by the poller.
        /// </summary>
        /// <remarks>Only transfers affecting the federation multisig wallet contract are processed.</remarks>
        /// <param name="blockHash">The hash of the block the transfer transaction appeared in.</param>
        /// <param name="transactionHash">The hash of the transaction that the transfer method call appeared in.</param>
        /// <param name="transferContractAddress">The address of the ERC20 contract that the transfer was actioned against.</param>
        /// <param name="transfer">The metadata of the transfer method call.</param>
        private async Task ProcessTransferAsync(string blockHash, string transactionHash, string transferContractAddress, TransferDetails transfer, KeyValuePair<DestinationChain, IETHClient> supportedChain)
        {
            this.logger.Info("Conversion transfer transaction '{0}' received from polled block '{1}', sender {2}.", transactionHash, blockHash, transfer.From);

            lock (this.repositoryLock)
            {
                if (this.conversionRequestRepository.Get(transactionHash) != null)
                {
                    this.logger.Info("Token transfer transaction '{0}' already exists, ignoring.", transactionHash);

                    return;
                }
            }

            this.logger.Info("Conversion transfer transaction '{0}' has value {1}.", transactionHash, transfer.Value);

            // Unlike processing WSTRAX burns, we cannot impose a different method signature on an arbitrary contract's transfer() method without breaking ERC20
            // compatibility. Therefore, we need to rely on the user having previously captured their desired destination address using the key value store contract.
            // Therefore every transfer transaction actually requires two transactions (unless one user performs multiple transfers with the same source address, in which
            // case the number of transactions will be 1 KVS transaction + N transfers).
            string destinationAddress = await supportedChain.Value.GetKeyValueStoreAsync(transfer.From, "CirrusDestinationAddress").ConfigureAwait(false);

            if (string.IsNullOrWhiteSpace(destinationAddress))
            {
                this.logger.Warn("Conversion transfer transaction '{0}' has no destination address!", transactionHash);

                return;
            }

            this.logger.Info("Conversion transfer transaction '{0}' has destination address {1}.", transactionHash, destinationAddress);

            // Validate that it is a Cirrus address here before bothering to add it to the repository.
            try
            {
                BitcoinAddress.Create(destinationAddress, this.network);
            }
            catch (Exception)
            {
                this.logger.Warn("Error validating destination address '{0}' for transaction '{1}'.", destinationAddress, transactionHash);

                return;
            }

            // Schedule this transaction to be processed at the next block height that is divisible by 100. 
            // Thus will round up to the nearest 100 then add another another 100.
            // In this way, transfers will always be scheduled at a predictable future time across the multisig.
            // This is because we cannot predict exactly when each node is polling the Ethereum chain for events.
            ulong blockHeight = (ulong)(this.chainIndexer.Tip.Height - (this.chainIndexer.Tip.Height % 50) + 100);

            if (blockHeight <= 0)
                blockHeight = 100;

            if (!this.interopSettings.GetSettingsByChain(supportedChain.Key).WatchedErc20Contracts.TryGetValue(transferContractAddress, out string destinationContract))
            {
                this.logger.Error("Unknown ERC20 contract address '{0}'; unable to map it to an SRC20 contract", transferContractAddress);
                return;
            }

            this.logger.Info($"Conversion transfer transaction '{transactionHash}' with transfer contract address {transferContractAddress} has destination contract destination address {destinationContract}.");

            lock (this.repositoryLock)
            {
                this.conversionRequestRepository.Save(new ConversionRequest()
                {
                    RequestId = transactionHash,
                    RequestType = ConversionRequestType.Mint,
                    Processed = false,
                    RequestStatus = ConversionRequestStatus.Unprocessed,
                    Amount = this.ConvertWeiToSatoshi(transfer.Value),
                    BlockHeight = (int)blockHeight,
                    DestinationAddress = destinationAddress,
                    DestinationChain = DestinationChain.CIRRUS,
                    TokenContract = destinationContract // Need to record the mapping of which Cirrus token contract the minting request should be actioned against.
                });
            }
        }

        /// <summary>Converting from wei to satoshi will result in a loss of precision past the 8th decimal place.</summary>
        /// <param name="wei">The number of wei to convert.</param>
        /// <returns>The equivalent number of satoshi corresponding to the number of wei.</returns>
        private ulong ConvertWeiToSatoshi(BigInteger wei)
        {
            decimal baseCurrencyUnits = Web3.Convert.FromWei(wei, UnitConversion.EthUnit.Ether);

            return Convert.ToUInt64(Money.Coins(baseCurrencyUnits).Satoshi);
        }

        /// <summary>
        /// Iterates through all unprocessed mint requests in the repository.
        /// If this node is regarded as the designated originator of the multisig transaction, it will submit the transfer transaction data to
        /// the multisig wallet contract on the Ethereum chain. This data consists of a method call to the transfer() method on the wrapped STRAX contract,
        /// as well as the intended recipient address and amount of tokens to be transferred.
        /// </summary>
        private async Task ProcessConversionRequestsAsync()
        {
            List<ConversionRequest> mintRequests;
            lock (this.repositoryLock)
            {
                mintRequests = this.conversionRequestRepository.GetAllMint(true);
            }

            if (mintRequests == null)
            {
                this.logger.Debug("There are no requests.");
                return;
            }

            this.logger.Info("There are {0} unprocessed conversion mint requests.", mintRequests.Count);

            foreach (ConversionRequest request in mintRequests)
            {
                // Ignore old conversion requests for the time being.
                if (request.RequestStatus == ConversionRequestStatus.Unprocessed && (this.chainIndexer.Tip.Height - request.BlockHeight) > this.network.Consensus.MaxReorgLength)
                {
                    this.logger.Info("Ignoring old conversion mint request '{0}' with status {1} from block height {2}.", request.RequestId, request.RequestStatus, request.BlockHeight);

                    request.Processed = true;

                    lock (this.repositoryLock)
                    {
                        this.conversionRequestRepository.Save(request);
                    }

                    continue;
                }

                bool isTransfer = false;
                if (!string.IsNullOrWhiteSpace(request.TokenContract) && request.DestinationChain == DestinationChain.CIRRUS)
                {
                    // This is an ERC20 -> SRC20 minting request, and therefore needs to be handled differently to wSTRAX.
                    this.logger.Info("Processing ERC20 to SRC20 transfer request {0}.", request.RequestId);
                    isTransfer = true;
                }

                bool originator = DetermineConversionRequestOriginator(request.BlockHeight, out IFederationMember designatedMember);

                IETHClient clientForDestChain = this.ethClientProvider.GetClientForChain(request.DestinationChain);

                if (!isTransfer)
                {
                    this.logger.Info("Processing conversion mint request {0} on {1} chain.", request.RequestId, request.DestinationChain);

                    // Regardless of whether we are the originator, this is a good time to check the multisig's remaining reserve
                    // token balance. It is necessary to maintain a reserve as mint transactions are many times more expensive than
                    // transfers. As we don't know precisely what value transactions are expected, the sole determining factor is
                    // whether the reserve has a large enough balance to service the current conversion request. If not, trigger a
                    // mint for a predetermined amount.

                    BigInteger balanceRemaining = await clientForDestChain.GetErc20BalanceAsync(this.interopSettings.GetSettingsByChain(request.DestinationChain).MultisigWalletAddress).ConfigureAwait(false);

                    // The request is denominated in satoshi and needs to be converted to wei.
                    BigInteger conversionAmountInWei = this.CoinsToWei(Money.Satoshis(request.Amount));

                    // We expect that every node will eventually enter this area of the code when the reserve balance is depleted.
                    if (conversionAmountInWei >= balanceRemaining)
                        await this.PerformReplenishmentAsync(request, conversionAmountInWei, balanceRemaining, originator).ConfigureAwait(false);
                }

                // The state machine gets shared between wSTRAX minting transactions, and ERC20/SRC20 transfers.
                // The main difference between the two is that we do not have an IEthClient for Cirrus, and have to make contract calls via the HTTP API.
                // TODO: Perhaps the transactionId coordination should actually be done within the multisig contract. This will however increase gas costs for each mint. Maybe a Cirrus contract instead?
                switch (request.RequestStatus)
                {
                    case ConversionRequestStatus.Unprocessed:
                        {
                            if (originator)
                            {
                                // If this node is the designated transaction originator, it must create and submit the transaction to the multisig.
                                this.logger.Info("This node selected as originator for transaction '{0}'.", request.RequestId);

                                request.RequestStatus = ConversionRequestStatus.OriginatorNotSubmitted;
                            }
                            else
                            {
                                this.logger.Info("This node was not selected as the originator for transaction '{0}'. The originator is: '{1}'.", request.RequestId, designatedMember == null ? "N/A (Overridden)" : designatedMember.PubKey?.ToHex());

                                request.RequestStatus = ConversionRequestStatus.NotOriginator;
                            }

                            break;
                        }

                    case ConversionRequestStatus.OriginatorNotSubmitted:
                        {
                            MultisigTransactionIdentifiers identifiers;

                            if (isTransfer)
                            {
                                // TODO: Make a Cirrus version of SubmitTransactionAsync that can handle more generic operations than just minting
                                identifiers = await this.cirrusClient.MintAsync(this.interopSettings.GetSettings<CirrusInteropSettings>().CirrusMultisigContractAddress, request.DestinationAddress, Money.Satoshis(request.Amount)).ConfigureAwait(false);
                            }
                            else
                            {
                                this.logger.Info("Conversion not yet submitted, checking which gas price to use.");

                                // First construct the necessary transfer() transaction data, utilising the ABI of the wrapped STRAX ERC20 contract.
                                // When this constructed transaction is actually executed, the transfer's source account will be the account executing the transaction i.e. the multisig contract address.
                                string abiData = clientForDestChain.EncodeTransferParams(request.DestinationAddress, this.CoinsToWei(Money.Satoshis(request.Amount)));

                                int gasPrice = this.externalApiPoller.GetGasPrice();

                                // If a gas price is not currently available then fall back to the value specified on the command line.
                                if (gasPrice == -1)
                                    gasPrice = this.interopSettings.GetSettingsByChain(request.DestinationChain).GasPrice;

                                this.logger.Info("Originator will use a gas price of {0} to submit the transaction.", gasPrice);

                                // Submit the unconfirmed transaction data to the multisig contract, returning a transactionId used to refer to it.
                                // Once sufficient multisig owners have confirmed the transaction the multisig contract will execute it.
                                // Note that by submitting the transaction to the multisig wallet contract, the originator is implicitly granting it one confirmation.
                                identifiers = await clientForDestChain.SubmitTransactionAsync(this.interopSettings.GetSettingsByChain(request.DestinationChain).WrappedStraxContractAddress, 0, abiData, gasPrice).ConfigureAwait(false);
                            }

                            if (identifiers.TransactionId == BigInteger.MinusOne)
                            {
                                this.logger.Error($"Minting on {request.DestinationChain} to address '{request.DestinationAddress}' for {request.Amount} failed: {identifiers.Message}");
                                request.RequestStatus = ConversionRequestStatus.Failed;
                                request.StatusMessage = identifiers.Message;

                                // TODO: Submitting the transaction failed, this needs to be handled
                            }
                            else
                                request.RequestStatus = ConversionRequestStatus.OriginatorSubmitting;

                            request.ExternalChainBlockHeight = identifiers.BlockHeight;
                            request.ExternalChainTxHash = identifiers.TransactionHash;
                            request.ExternalChainTxEventId = identifiers.TransactionId.ToString();

                            break;
                        }
                    case ConversionRequestStatus.OriginatorSubmitting:
                        {
                            (BigInteger confirmationCount, string blockHash) = isTransfer ? this.cirrusClient.GetConfirmations(request.ExternalChainBlockHeight) : await clientForDestChain.GetConfirmationsAsync(request.ExternalChainTxHash).ConfigureAwait(false);

                            this.logger.Info($"Originator confirming transaction id '{request.ExternalChainTxHash}' '({request.ExternalChainTxEventId})' before broadcasting; confirmations: {confirmationCount}; Block Hash {blockHash}.");

                            if (confirmationCount < this.SubmissionConfirmationThreshold)
                                break;

                            this.logger.Info("Originator submitted transaction to multisig in transaction '{0}' and was allocated transactionId '{1}'.", request.ExternalChainTxHash, request.ExternalChainTxEventId);

                            this.conversionRequestCoordinationService.AddVote(request.RequestId, BigInteger.Parse(request.ExternalChainTxEventId), this.federationManager.CurrentFederationKey.PubKey);

                            request.RequestStatus = ConversionRequestStatus.OriginatorSubmitted;

                            break;
                        }

                    case ConversionRequestStatus.OriginatorSubmitted:
                        {
                            // It must then propagate the transactionId to the other nodes so that they know they should confirm it.
                            // The reason why each node doesn't simply maintain its own transaction counter, is that it can't be guaranteed
                            // that a transaction won't be submitted out-of-turn by a rogue or malfunctioning federation multisig node.
                            // The coordination mechanism safeguards against this, as any such spurious transaction will not receive acceptance votes.
                            // TODO: The transactionId should be accompanied by the hash of the submission transaction on the Ethereum chain so that it can be verified

                            BigInteger transactionId2 = this.conversionRequestCoordinationService.GetCandidateTransactionId(request.RequestId);

                            if (transactionId2 != BigInteger.MinusOne)
                            {
                                await this.BroadcastCoordinationVoteRequestAsync(request.RequestId, transactionId2, request.DestinationChain, isTransfer).ConfigureAwait(false);

                                BigInteger agreedTransactionId = this.conversionRequestCoordinationService.GetAgreedTransactionId(request.RequestId, this.interopSettings.GetSettingsByChain(request.DestinationChain).MultisigWalletQuorum);

                                if (agreedTransactionId != BigInteger.MinusOne)
                                {
                                    this.logger.Info("Transaction '{0}' has received sufficient votes, it should now start getting confirmed by each peer.", agreedTransactionId);

                                    request.RequestStatus = ConversionRequestStatus.VoteFinalised;
                                }
                            }

                            break;
                        }

                    case ConversionRequestStatus.VoteFinalised:
                        {
                            BigInteger transactionId3 = this.conversionRequestCoordinationService.GetAgreedTransactionId(request.RequestId, this.interopSettings.GetSettingsByChain(request.DestinationChain).MultisigWalletQuorum);

                            if (transactionId3 != BigInteger.MinusOne)
                            {
                                // The originator isn't responsible for anything further at this point, except for periodically checking the confirmation count.
                                // The non-originators also need to monitor the confirmation count so that they know when to mark the transaction as processed locally.
                                BigInteger confirmationCount = isTransfer ? await this.cirrusClient.GetMultisigConfirmationCountAsync(transactionId3, (ulong)this.chainIndexer.Tip.Height).ConfigureAwait(false) : await clientForDestChain.GetMultisigConfirmationCountAsync(transactionId3).ConfigureAwait(false);

                                if (confirmationCount >= this.interopSettings.GetSettingsByChain(request.DestinationChain).MultisigWalletQuorum)
                                {
                                    this.logger.Info("Transaction '{0}' has received at least {1} confirmations, it will be automatically executed by the multisig contract.", transactionId3, this.interopSettings.GetSettingsByChain(request.DestinationChain).MultisigWalletQuorum);

                                    request.RequestStatus = ConversionRequestStatus.Processed;
                                    request.Processed = true;

                                    // We no longer need to track votes for this transaction.
                                    this.conversionRequestCoordinationService.RemoveTransaction(request.RequestId);
                                }
                                else
                                {
                                    this.logger.Info("Transaction '{0}' has finished voting but does not yet have {1} confirmations, re-broadcasting votes to peers.", transactionId3, this.interopSettings.GetSettingsByChain(request.DestinationChain).MultisigWalletQuorum);
                                    // There are not enough confirmations yet.
                                    // Even though the vote is finalised, other nodes may come and go. So we re-broadcast the finalised votes to all federation peers.
                                    // Nodes will simply ignore the messages if they are not relevant.

                                    await this.BroadcastCoordinationVoteRequestAsync(request.RequestId, transactionId3, request.DestinationChain, isTransfer).ConfigureAwait(false);

                                    // No state transition here, we are waiting for sufficient confirmations.
                                }
                            }

                            break;
                        }
                    case ConversionRequestStatus.NotOriginator:
                        {
                            // If not the originator, this node needs to determine what multisig wallet transactionId it should confirm.
                            // Initially there will not be a quorum of nodes that agree on the transactionId.
                            // So each node needs to satisfy itself that the transactionId sent by the originator exists in the multisig wallet.
                            // This is done within the InteropBehavior automatically, we just check each poll loop if a transaction has enough votes yet.
                            // Each node must only ever confirm a single transactionId for a given conversion transaction.
                            BigInteger agreedUponId = this.conversionRequestCoordinationService.GetAgreedTransactionId(request.RequestId, this.interopSettings.GetSettingsByChain(request.DestinationChain).MultisigWalletQuorum);

                            if (agreedUponId != BigInteger.MinusOne)
                            {
                                // TODO: Should we check the number of confirmations for the submission transaction here too?

                                this.logger.Info("Quorum reached for conversion transaction '{0}' with transactionId '{1}', submitting confirmation to contract.", request.RequestId, agreedUponId);

                                string confirmationHash;
                                if (isTransfer)
                                {
                                    (string TransactionHash, string Message) result = await this.cirrusClient.ConfirmTransactionAsync(agreedUponId).ConfigureAwait(false);

                                    // TODO: This needs to be doner better.
                                    if (!string.IsNullOrEmpty(result.Message) && result.Message != "Cannot confirm an already-executed transaction.")
                                    {
                                        this.logger.Error(result.Message);

                                        request.StatusMessage = result.Message;
                                        request.RequestStatus = ConversionRequestStatus.Failed;

                                        break;
                                    }
                                    else
                                        confirmationHash = result.TransactionHash;
                                }
                                else
                                {
                                    int gasPrice = this.externalApiPoller.GetGasPrice();

                                    // If a gas price is not currently available then fall back to the value specified on the command line.
                                    if (gasPrice == -1)
                                        gasPrice = this.interopSettings.GetSettingsByChain(request.DestinationChain).GasPrice;

                                    this.logger.Info("The non-originator will use a gas price of {0} to confirm the transaction.", gasPrice);

                                    // Once a quorum is reached, each node confirms the agreed transactionId.
                                    // If the originator or some other nodes renege on their vote, the current node will not re-confirm a different transactionId.
                                    confirmationHash = await clientForDestChain.ConfirmTransactionAsync(agreedUponId, gasPrice).ConfigureAwait(false);
                                }

                                request.ExternalChainTxHash = confirmationHash;

                                this.logger.Info("The hash of the confirmation transaction for conversion transaction '{0}' was '{1}'.", request.RequestId, confirmationHash);

                                request.RequestStatus = ConversionRequestStatus.VoteFinalised;
                            }
                            else
                            {
                                BigInteger transactionId4 = this.conversionRequestCoordinationService.GetCandidateTransactionId(request.RequestId);

                                if (transactionId4 != BigInteger.MinusOne)
                                {
                                    this.logger.Debug("Broadcasting vote (transactionId '{0}') for conversion transaction '{1}'.", transactionId4, request.RequestId);

                                    this.conversionRequestCoordinationService.AddVote(request.RequestId, transactionId4, this.federationManager.CurrentFederationKey.PubKey);

                                    await this.BroadcastCoordinationVoteRequestAsync(request.RequestId, transactionId4, request.DestinationChain, isTransfer).ConfigureAwait(false);
                                }

                                // No state transition here, as we are waiting for the candidate transactionId to progress to an agreed upon transactionId via a quorum.
                            }

                            break;
                        }
                }

                // Make sure that any state transitions are persisted to storage.
                lock (this.repositoryLock)
                {
                    this.conversionRequestRepository.Save(request);
                }

                // Unlike the mint requests, burns are not initiated by the multisig wallet.
                // Instead they are initiated by the user, via a contract call to the burn() method on the WrappedStrax contract.
                // They need to provide a destination STRAX address when calling the burn method.

                // Properly processing burn transactions requires emulating a withdrawal on the main chain from the multisig wallet.
                // It will be easier when conversion can be done directly to and from a Cirrus contract instead.
            }
        }

        /// <summary>
        /// Determines the originator of the conversion request. It can either be this node or another multisig member.
        /// <para>
        /// Multisig members on CirrusTest can use the -overrideoriginator command line parameter to determine who
        /// the originator is due to the fact that not all of the multisig members are online.
        /// </para>
        /// </summary>
        /// <param name="blockHeight">The block height of the conversion request.</param>
        /// <param name="designatedMember">The federation member who is assigned as the originator of this conversion transaction.</param>
        /// <returns><c>true</c> if this node is selected as the originator.</returns>
        private bool DetermineConversionRequestOriginator(int blockHeight, out IFederationMember designatedMember)
        {
            designatedMember = null;

            if (this.interopSettings.OverrideOriginatorEnabled)
            {
                if (this.interopSettings.OverrideOriginator)
                {
                    designatedMember = this.federationManager.GetCurrentFederationMember();
                    return true;
                }

                return false;
            }

            // We are not able to simply use the entire federation member list, as only multisig nodes can be transaction originators.
            List<IFederationMember> federation = this.federationHistory.GetFederationForBlock(this.chainIndexer.GetHeader(blockHeight));

            this.logger.Info($"Federation retrieved at height '{blockHeight}', size {federation.Count} members.");

            var multisig = new List<CollateralFederationMember>();

            foreach (IFederationMember member in federation)
            {
                if (!(member is CollateralFederationMember collateralMember))
                    continue;

                if (!collateralMember.IsMultisigMember)
                    continue;

                if (!MultiSigMembers.IsContractOwner(this.network, collateralMember.PubKey))
                    continue;

                multisig.Add(collateralMember);
            }

            // This should be impossible.
            if (multisig.Count == 0)
                throw new InteropException("There are no multisig members.");

            designatedMember = multisig[blockHeight % multisig.Count];
            return designatedMember.Equals(this.federationManager.GetCurrentFederationMember());
        }

        /// <summary>
        /// Wait for the submission to be well-confirmed before initial vote and broadcast.
        /// </summary>
        /// <param name="identifiers">The transaction information to check.</param>
        /// <param name="destinationChain">The chain that the confirmation count should be checked on.</param>
        /// <param name="caller">The caller that is waiting on the submission transaction's confirmation count.</param>
        /// <returns><c>True if it succeeded</c>, <c>false</c> if the node is stopping.</returns>
        private async Task<bool> WaitForReplenishmentToBeConfirmedAsync(MultisigTransactionIdentifiers identifiers, DestinationChain destinationChain, [CallerMemberName] string caller = null)
        {
            while (true)
            {
                if (this.nodeLifetime.ApplicationStopping.IsCancellationRequested)
                    return false;

                (BigInteger confirmationCount, string blockHash) = await this.ethClientProvider.GetClientForChain(destinationChain).GetConfirmationsAsync(identifiers.TransactionHash).ConfigureAwait(false);
                this.logger.Info($"[{caller}] Originator confirming transaction id '{identifiers.TransactionHash}' '({identifiers.TransactionId})' before broadcasting; confirmations: {confirmationCount}; Block Hash {blockHash}.");

                if (confirmationCount >= this.SubmissionConfirmationThreshold)
                    break;

                await Task.Delay(TimeSpan.FromSeconds(2)).ConfigureAwait(false);
            }

            return true;
        }

        private async Task PerformReplenishmentAsync(ConversionRequest request, BigInteger amountInWei, BigInteger startBalance, bool originator)
        {
            // We need a 'request ID' for the minting that is a) different from the current request ID and b) always unique so that transaction ID votes are unique to this minting.
            string mintRequestId;

            // So, just hash the request ID once. This way all nodes will have the same request ID for this mint.
            using (var hs = new HashStream())
            {
                var bs = new BitcoinStream(hs, true);
                bs.ReadWrite(uint256.Parse(request.RequestId));

                mintRequestId = hs.GetHash().ToString();
            }

            // Only the originator initially knows what value this gets set to after submission, until voting is concluded.
            BigInteger mintTransactionId = BigInteger.MinusOne;

            if (originator)
            {
                this.logger.Info("Insufficient reserve balance remaining, initiating mint transaction to replenish reserve.");

                // By minting the request amount + the reserve requirement, we cater for arbitrarily large amounts in the request.
                string mintData = this.ethClientProvider.GetClientForChain(request.DestinationChain).EncodeMintParams(this.interopSettings.GetSettingsByChain(request.DestinationChain).MultisigWalletAddress, amountInWei + this.ReserveBalanceTarget);

                int gasPrice = this.externalApiPoller.GetGasPrice();

                // If a gas price is not currently available then fall back to the value specified on the command line.
                if (gasPrice == -1)
                    gasPrice = this.interopSettings.GetSettingsByChain(request.DestinationChain).GasPrice;

                this.logger.Info("Originator will use a gas price of {0} to submit the mint replenishment transaction.", gasPrice);

                MultisigTransactionIdentifiers identifiers = await this.ethClientProvider.GetClientForChain(request.DestinationChain).SubmitTransactionAsync(this.interopSettings.GetSettingsByChain(request.DestinationChain).WrappedStraxContractAddress, 0, mintData, gasPrice).ConfigureAwait(false);

                if (identifiers.TransactionId == BigInteger.MinusOne)
                {
                    // TODO: Submission failed, what should we do here? Note that retrying could incur additional transaction fees depending on the nature of the failure
                }
                else
                {
                    mintTransactionId = identifiers.TransactionId;

                    if (!await WaitForReplenishmentToBeConfirmedAsync(identifiers, request.DestinationChain).ConfigureAwait(false))
                        return;
                }

                this.logger.Info("Originator adding its vote for mint transaction id: {0}", mintTransactionId);

                this.conversionRequestCoordinationService.AddVote(mintRequestId, mintTransactionId, this.federationManager.CurrentFederationKey.PubKey);

                // Now we need to broadcast the mint transactionId to the other multisig nodes so that they can sign it off.
                // TODO: The other multisig nodes must be careful not to blindly trust that any given transactionId relates to a mint transaction. Need to validate the recipient

                await this.BroadcastCoordinationVoteRequestAsync(mintRequestId, mintTransactionId, request.DestinationChain, false).ConfigureAwait(false);
            }
            else
                this.logger.Info("Insufficient reserve balance remaining, waiting for originator to initiate mint transaction to replenish reserve.");

            BigInteger agreedTransactionId;

            // For non-originators to keep track of the ID they are intending to use.
            BigInteger ourTransactionId = BigInteger.MinusOne;

            while (true)
            {
                if (this.nodeLifetime.ApplicationStopping.IsCancellationRequested)
                    return;

                agreedTransactionId = this.conversionRequestCoordinationService.GetAgreedTransactionId(mintRequestId, this.interopSettings.GetSettingsByChain(request.DestinationChain).MultisigWalletQuorum);

                this.logger.Debug("Agreed transaction id '{0}'.", agreedTransactionId);

                if (agreedTransactionId != BigInteger.MinusOne)
                    break;

                // Just re-broadcast.
                if (originator)
                {
                    this.logger.Debug("Originator broadcasting id {0}.", mintTransactionId);

                    await this.BroadcastCoordinationVoteRequestAsync(mintRequestId, mintTransactionId, request.DestinationChain, false).ConfigureAwait(false);
                }
                else
                {
                    if (ourTransactionId == BigInteger.MinusOne)
                        ourTransactionId = this.conversionRequestCoordinationService.GetCandidateTransactionId(mintRequestId);

                    this.logger.Debug("Non-originator broadcasting id {0}.", ourTransactionId);

                    if (ourTransactionId != BigInteger.MinusOne)
                    {
                        // Wait for the submission of the transaction to be confirmed on the actual chain.

                        this.conversionRequestCoordinationService.AddVote(mintRequestId, ourTransactionId, this.federationManager.CurrentFederationKey.PubKey);

                        // Broadcast our vote.
                        await this.BroadcastCoordinationVoteRequestAsync(mintRequestId, ourTransactionId, request.DestinationChain, false).ConfigureAwait(false);
                    }
                }

                await Task.Delay(TimeSpan.FromSeconds(2)).ConfigureAwait(false);
            }

            this.logger.Info("Agreed transaction ID for replenishment transaction: {0}", agreedTransactionId);

            if (!originator)
            {
                int gasPrice = this.externalApiPoller.GetGasPrice();

                // If a gas price is not currently available then fall back to the value specified on the command line.
                if (gasPrice == -1)
                    gasPrice = this.interopSettings.GetSettingsByChain(request.DestinationChain).GasPrice;

                this.logger.Info("Non-originator will use a gas price of {0} to confirm the mint replenishment transaction.", gasPrice);

                string confirmation = await this.ethClientProvider.GetClientForChain(request.DestinationChain).ConfirmTransactionAsync(agreedTransactionId, gasPrice).ConfigureAwait(false);

                this.logger.Info("ID of confirmation transaction: {0}", confirmation);
            }

            while (true)
            {
                if (this.nodeLifetime.ApplicationStopping.IsCancellationRequested)
                    return;

                BigInteger confirmationCount = await this.ethClientProvider.GetClientForChain(request.DestinationChain).GetMultisigConfirmationCountAsync(agreedTransactionId).ConfigureAwait(false);

                this.logger.Info("Waiting for confirmation of mint replenishment transaction {0}, current count {1}.", mintRequestId, confirmationCount);

                if (confirmationCount >= this.interopSettings.GetSettingsByChain(request.DestinationChain).MultisigWalletQuorum)
                    break;

                // TODO: Maybe this should eventually age out?
                await Task.Delay(TimeSpan.FromSeconds(2)).ConfigureAwait(false);
            }

            this.logger.Info("Mint replenishment transaction {0} fully confirmed.", mintTransactionId);

            while (true)
            {
                if (this.nodeLifetime.ApplicationStopping.IsCancellationRequested)
                    return;

                BigInteger balance = await this.ethClientProvider.GetClientForChain(request.DestinationChain).GetErc20BalanceAsync(this.interopSettings.GetSettingsByChain(request.DestinationChain).MultisigWalletAddress).ConfigureAwait(false);

                if (balance > startBalance)
                {
                    this.logger.Info("The contract's balance has been replenished, new balance {0}.", balance);
                    break;
                }
                else
                    this.logger.Info("The contract's balance is unchanged at {0}.", balance);

                await Task.Delay(TimeSpan.FromSeconds(2)).ConfigureAwait(false);
            }
        }

        private async Task BroadcastCoordinationVoteRequestAsync(string requestId, BigInteger transactionId, DestinationChain destinationChain, bool isTransfer)
        {
            string signature = this.federationManager.CurrentFederationKey.SignMessage(requestId + ((int)transactionId));
            await this.federatedPegBroadcaster.BroadcastAsync(ConversionRequestPayload.Request(requestId, (int)transactionId, signature, destinationChain, isTransfer)).ConfigureAwait(false);
        }

        private BigInteger CoinsToWei(Money coins)
        {
            BigInteger baseCurrencyUnits = Web3.Convert.ToWei(coins.ToUnit(MoneyUnit.BTC), UnitConversion.EthUnit.Ether);
            return baseCurrencyUnits;
        }

        private void AddComponentStats(StringBuilder benchLog)
        {
            if (this.interopSettings.OverrideOriginatorEnabled)
            {
                var isOriginatorOverridden = this.interopSettings.OverrideOriginator ? "Yes" : "No";
                benchLog.AppendLine($">> InterFlux Mint Requests (last 5) [Originator Overridden : {isOriginatorOverridden}]");
            }
            else
                benchLog.AppendLine(">> InterFlux Mint Requests (last 5) [Dynamic Originator]");

            List<ConversionRequest> requests;
            lock (this.repositoryLock)
            {
                requests = this.conversionRequestRepository.GetAllMint(false).OrderByDescending(i => i.BlockHeight).Take(5).ToList();
            }

            foreach (ConversionRequest request in requests)
            {
                benchLog.AppendLine($"Destination: {request.DestinationAddress.Substring(0, 10)}... Id: {request.RequestId} Chain: {request.DestinationChain} Status: {request.RequestStatus} Amount: {new Money(request.Amount)} Ext Hash: {request.ExternalChainTxHash}");
            }

            benchLog.AppendLine();
            benchLog.AppendLine(">> InterFlux Burn Requests (last 5)");

            lock (this.repositoryLock)
            {
                requests = this.conversionRequestRepository.GetAllBurn(false).OrderByDescending(i => i.BlockHeight).Take(5).ToList();
            }
            foreach (ConversionRequest request in requests)
            {
                benchLog.AppendLine($"Destination: {request.DestinationAddress.Substring(0, 10)}... Id: {request.RequestId} Status: {request.RequestStatus} Processed: {request.Processed} Amount: {new Money(request.Amount)} Height: {request.BlockHeight}");
            }

            benchLog.AppendLine();
        }

        /// <inheritdoc />
        public void Dispose()
        {
            this.interopLoop?.Dispose();
            this.conversionLoop?.Dispose();
            this.conversionBurnTransferLoop?.Dispose();
        }
    }
}
