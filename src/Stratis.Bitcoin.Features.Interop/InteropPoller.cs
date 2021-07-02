﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using NBitcoin;
using NBitcoin.Crypto;
using Nethereum.Contracts;
using Nethereum.Util;
using Nethereum.Web3;
using Stratis.Bitcoin.AsyncWork;
using Stratis.Bitcoin.Configuration;
using Stratis.Bitcoin.Features.ExternalApi;
using Stratis.Bitcoin.Features.Interop.ETHClient;
using Stratis.Bitcoin.Features.Interop.Exceptions;
using Stratis.Bitcoin.Features.Interop.Payloads;
using Stratis.Bitcoin.Features.PoA;
using Stratis.Bitcoin.Interfaces;
using Stratis.Bitcoin.Utilities;
using Stratis.Features.Collateral.CounterChain;
using Stratis.Features.FederatedPeg.Conversion;
using Stratis.Features.FederatedPeg.Coordination;
using Stratis.Features.FederatedPeg.Interfaces;

namespace Stratis.Bitcoin.Features.Interop
{
    public sealed class InteropPoller : IDisposable
    {
        /// <summary>1x10^24 wei = 1 000 000 tokens</summary>
        public BigInteger ReserveBalanceTarget = BigInteger.Parse("1000000000000000000000000");

        /// <summary>The number of blocks deep a submission transaction needs to be before it should start getting confirmed by the non-originating nodes.</summary>
        public readonly BigInteger SubmissionConfirmationThreshold = 3;

        private readonly IAsyncProvider asyncProvider;
        private readonly ChainIndexer chainIndexer;
        private readonly Network counterChainNetwork;
        private readonly IConversionRequestCoordinationService conversionRequestCoordinationService;
        private readonly IConversionRequestRepository conversionRequestRepository;
        private readonly IExternalApiPoller externalApiPoller;
        private readonly IETHClient ETHClientBase;
        private readonly IFederationManager federationManager;
        private readonly IFederationHistory federationHistory;
        private readonly IFederatedPegBroadcaster federatedPegBroadcaster;
        private readonly InteropSettings interopSettings;
        private readonly IInitialBlockDownloadState initialBlockDownloadState;
        private readonly ILogger logger;
        private readonly Network network;
        private readonly INodeLifetime nodeLifetime;

        private IAsyncLoop interopLoop;
        private IAsyncLoop conversionLoop;

        private bool firstPoll;

        public InteropPoller(
            IAsyncProvider asyncProvider,
            ChainIndexer chainIndexer,
            IConversionRequestRepository conversionRequestRepository,
            IConversionRequestCoordinationService conversionRequestCoordinationService,
            CounterChainNetworkWrapper counterChainNetworkWrapper,
            IETHClient ethClientBase,
            IExternalApiPoller externalApiPoller,
            IFederationHistory federationHistory,
            IFederationManager federationManager,
            IFederatedPegBroadcaster federatedPegBroadcaster,
            IInitialBlockDownloadState initialBlockDownloadState,
            InteropSettings interopSettings,
            NodeSettings nodeSettings,
            INodeStats nodeStats,
            INodeLifetime nodeLifetime)
        {
            this.interopSettings = interopSettings;
            this.ETHClientBase = ethClientBase;
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
            this.logger = nodeSettings.LoggerFactory.CreateLogger(this.GetType().FullName);

            this.firstPoll = true;

            nodeStats.RegisterStats(this.AddComponentStats, StatsType.Component, this.GetType().Name, 250);
        }

        /// <summary>
        /// Initializes the poller by starting the periodic loops thats checks for and processes the conversion requests.
        /// </summary>
        public void Initialize()
        {
            if (!this.interopSettings.InteropEnabled)
                return;

            if (!this.federationManager.IsFederationMember)
                return;

            this.logger.LogInformation($"Interoperability enabled, initializing periodic loop.");

            // Initialize the interop polling loop, to check for interop contract requests.
            this.interopLoop = this.asyncProvider.CreateAndRunAsyncLoop("PeriodicCheckInterop", async (cancellation) =>
            {
                if (this.initialBlockDownloadState.IsInitialBlockDownload())
                    return;

                this.logger.LogTrace("Beginning interop loop.");

                try
                {
                    await this.CheckETHNodeAsync().ConfigureAwait(false);
                }
                catch (Exception e)
                {
                    this.logger.LogWarning("Exception raised when checking interop requests. {0}", e);
                }

                this.logger.LogTrace("Finishing interop loop.");
            },
            this.nodeLifetime.ApplicationStopping,
            repeatEvery: TimeSpans.TenSeconds,
            startAfter: TimeSpans.Second);

            // Initialize the conversion polling loop, to check for conversion requests.
            this.conversionLoop = this.asyncProvider.CreateAndRunAsyncLoop("PeriodicCheckConversionStore", async (cancellation) =>
            {
                if (this.initialBlockDownloadState.IsInitialBlockDownload())
                    return;

                this.logger.LogTrace("Beginning conversion processing loop.");

                try
                {
                    await this.CheckForContractEventsAsync().ConfigureAwait(false);
                    await this.ProcessConversionRequestsAsync().ConfigureAwait(false);
                }
                catch (Exception e)
                {
                    this.logger.LogWarning($"Exception raised when checking conversion requests. {e}");
                }

                this.logger.LogTrace("Finishing conversion processing loop.");
            },
            this.nodeLifetime.ApplicationStopping,
            repeatEvery: TimeSpans.TenSeconds,
            startAfter: TimeSpans.Second);
        }

        /// <summary>
        /// Retrieves the current Ethereum chain height and account balance via the RPC interface to geth.
        /// </summary>
        private async Task CheckETHNodeAsync()
        {
            try
            {
                BigInteger blockHeight = await this.ETHClientBase.GetBlockHeightAsync().ConfigureAwait(false);
                BigInteger balance = await this.ETHClientBase.GetBalanceAsync(this.interopSettings.ETHAccount).ConfigureAwait(false);

                this.logger.LogInformation("Current Ethereum node block height is {0}. Balance for {1} is {2}", blockHeight, this.interopSettings.ETHAccount, balance);
            }
            catch (Exception e)
            {
                this.logger.LogError("Error checking Ethereum node status: {0}", e);
            }
        }

        /// <summary>
        /// Retrieves any Transfer events from the logs of the Wrapped Strax contract.
        /// Transfers with the zero (0x0000...) address as their destination can be considered to be burn transactions and are saved for processing as withdrawals on the mainchain.
        /// </summary>
        private async Task CheckForContractEventsAsync()
        {
            if (this.firstPoll)
            {
                // The filter should only be set up once IBD completes.
                await this.ETHClientBase.CreateTransferEventFilterAsync().ConfigureAwait(false);

                this.firstPoll = false;
            }

            // Check for all Transfer events against the WrappedStrax contract since the last time we checked.
            // In future this could also poll for other events as the need arises.
            List<EventLog<TransferEventDTO>> transferEvents = await this.ETHClientBase.GetTransferEventsForWrappedStraxAsync().ConfigureAwait(false);

            foreach (EventLog<TransferEventDTO> transferEvent in transferEvents)
            {
                // Will probably never be the case, but check anyway.
                if (string.IsNullOrWhiteSpace(transferEvent.Log.TransactionHash))
                    continue;

                // These could be mints or something else, either way ignore them.
                if (transferEvent.Event.From == ETHClient.ETHClient.ZeroAddress)
                    continue;

                // Transfers can only be burns if they are made with the zero address as the destination.
                if (transferEvent.Event.To != ETHClient.ETHClient.ZeroAddress)
                    continue;

                this.logger.LogInformation("Conversion burn transaction {0} received from contract events, sender {1}.", transferEvent.Log.TransactionHash, transferEvent.Event.From);

                if (this.conversionRequestRepository.Get(transferEvent.Log.TransactionHash) != null)
                {
                    this.logger.LogInformation("Conversion burn transaction {0} already exists, ignoring.", transferEvent.Log.TransactionHash);

                    continue;
                }

                if (transferEvent.Event.Value == BigInteger.Zero)
                {
                    this.logger.LogInformation("Ignoring zero-valued burn transaction {0}.", transferEvent.Log.TransactionHash);

                    continue;
                }

                if (transferEvent.Event.Value < BigInteger.Zero)
                {
                    this.logger.LogInformation("Ignoring negative-valued burn transaction {0}.", transferEvent.Log.TransactionHash);

                    continue;
                }

                this.logger.LogInformation("Conversion burn transaction {0} has value {1}.", transferEvent.Log.TransactionHash, transferEvent.Event.Value);

                // Look up the desired destination address for this account.
                string destinationAddress = await this.ETHClientBase.GetDestinationAddressAsync(transferEvent.Event.From).ConfigureAwait(false);

                this.logger.LogInformation("Conversion burn transaction {0} has destination address {1}.", transferEvent.Log.TransactionHash, destinationAddress);

                // Validate that it is a mainchain address here before bothering to add it to the repository.
                BitcoinAddress parsedAddress;
                try
                {
                    parsedAddress = BitcoinAddress.Create(destinationAddress, this.counterChainNetwork);
                }
                catch (Exception)
                {
                    this.logger.LogWarning("Error validating destination address {0} for transaction {1}.", destinationAddress, transferEvent.Log.TransactionHash);

                    continue;
                }

                // Schedule this transaction to be processed at the next block height that is divisible by 10. If the current block height is divisible by 10, add a further 10 to it.
                // In this way, burns will always be scheduled at a predictable future time across the multisig.
                // This is because we cannot predict exactly when each node is polling the Ethereum chain for events.
                ulong blockHeight = (ulong)this.chainIndexer.Tip.Height - ((ulong)this.chainIndexer.Tip.Height % 10) + 10;

                if (blockHeight <= 0)
                    blockHeight = 10;

                this.conversionRequestRepository.Save(new ConversionRequest()
                {
                    RequestId = transferEvent.Log.TransactionHash,
                    RequestType = ConversionRequestType.Burn,
                    Processed = false,
                    RequestStatus = ConversionRequestStatus.Unprocessed,
                    Amount = this.ConvertWeiToSatoshi(transferEvent.Event.Value),
                    BlockHeight = (int)blockHeight,
                    DestinationAddress = destinationAddress,
                    DestinationChain = DestinationChain.ETH // TODO only ETH supported for now. 
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
            List<ConversionRequest> mintRequests = this.conversionRequestRepository.GetAllMint(true);

            if (mintRequests == null)
                return;

            this.logger.LogInformation("There are {0} unprocessed conversion mint requests.", mintRequests.Count);

            foreach (ConversionRequest request in mintRequests)
            {
                // Ignore old conversion requests for the time being.
                if ((this.chainIndexer.Tip.Height - request.BlockHeight) > this.network.Consensus.MaxReorgLength)
                {
                    this.logger.LogInformation("Ignoring conversion mint request {0} with status {1} from block height {2}.", request.RequestId, request.RequestStatus, request.BlockHeight);

                    request.Processed = true;

                    this.conversionRequestRepository.Save(request);

                    continue;
                }

                this.logger.LogInformation("Processing conversion mint request {0}, status {1}.", request.RequestId, request.RequestStatus);

                var originator = DetermineConversionRequestOriginator(request.BlockHeight, out IFederationMember designatedMember);

                // Regardless of whether we are the originator, this is a good time to check the multisig's remaining reserve
                // token balance. It is necessary to maintain a reserve as mint transactions are many times more expensive than
                // transfers. As we don't know precisely what value transactions are expected, the sole determining factor is
                // whether the reserve has a large enough balance to service the current conversion request. If not, trigger a
                // mint for a predetermined amount.
                BigInteger balanceRemaining = await this.ETHClientBase.GetErc20BalanceAsync(this.interopSettings.ETHMultisigWalletAddress).ConfigureAwait(false);

                // The request is denominated in satoshi and needs to be converted to wei.
                BigInteger conversionAmountInWei = this.CoinsToWei(Money.Satoshis(request.Amount));

                // We expect that every node will eventually enter this area of the code when the reserve balance is depleted.
                if (conversionAmountInWei >= balanceRemaining)
                    await this.PerformReplenishmentAsync(request, conversionAmountInWei, balanceRemaining, originator).ConfigureAwait(false);

                // TODO: Perhaps the transactionId coordination should actually be done within the multisig contract. This will however increase gas costs for each mint. Maybe a Cirrus contract instead?
                switch (request.RequestStatus)
                {
                    case ConversionRequestStatus.Unprocessed:
                        {
                            if (originator)
                            {
                                // If this node is the designated transaction originator, it must create and submit the transaction to the multisig.
                                this.logger.LogInformation("This node selected as originator for transaction {0}.", request.RequestId);

                                request.RequestStatus = ConversionRequestStatus.OriginatorNotSubmitted;
                            }
                            else
                            {
                                this.logger.LogInformation("This node was not selected as the originator for transaction {0}. The originator is: {1}.", request.RequestId, designatedMember == null ? "N/A (test)" : designatedMember.PubKey?.ToHex());

                                request.RequestStatus = ConversionRequestStatus.NotOriginator;
                            }

                            break;
                        }

                    case ConversionRequestStatus.OriginatorNotSubmitted:
                        {
                            this.logger.LogInformation("Conversion not yet submitted, checking which gas price to use.");

                            // First construct the necessary transfer() transaction data, utilising the ABI of the wrapped STRAX ERC20 contract.
                            // When this constructed transaction is actually executed, the transfer's source account will be the account executing the transaction i.e. the multisig contract address.
                            string abiData = this.ETHClientBase.EncodeTransferParams(request.DestinationAddress, conversionAmountInWei);

                            int gasPrice = this.externalApiPoller.GetGasPrice();

                            // If a gas price is not currently available then fall back to the value specified on the command line.
                            if (gasPrice == -1)
                                gasPrice = this.interopSettings.ETHGasPrice;

                            this.logger.LogInformation("Originator will use a gas price of {0} to submit the transaction.", gasPrice);

                            // Submit the unconfirmed transaction data to the multisig contract, returning a transactionId used to refer to it.
                            // Once sufficient multisig owners have confirmed the transaction the multisig contract will execute it.
                            // Note that by submitting the transaction to the multisig wallet contract, the originator is implicitly granting it one confirmation.
                            MultisigTransactionIdentifiers identifiers = await this.ETHClientBase.SubmitTransactionAsync(this.interopSettings.ETHWrappedStraxContractAddress, 0, abiData, gasPrice).ConfigureAwait(false);
                            BigInteger transactionId = identifiers.TransactionId;

                            if (transactionId == BigInteger.MinusOne)
                            {
                                // TODO: Submitting the transaction failed, this needs to be handled
                            }

                            request.RequestEthTransactionHash = identifiers.TransactionHash;

                            if (!await WaitForTransactionToBeConfirmedAsync(identifiers).ConfigureAwait(false))
                                return;

                            this.logger.LogInformation("Originator submitted transaction to multisig in transaction {0} and was allocated transactionId {1}.", identifiers.TransactionHash, transactionId);

                            this.conversionRequestCoordinationService.AddVote(request.RequestId, transactionId, this.federationManager.CurrentFederationKey.PubKey);

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
                                await this.BroadcastCoordinationAsync(request.RequestId, transactionId2).ConfigureAwait(false);

                                BigInteger agreedTransactionId = this.conversionRequestCoordinationService.GetAgreedTransactionId(request.RequestId, this.interopSettings.ETHMultisigWalletQuorum);

                                if (agreedTransactionId != BigInteger.MinusOne)
                                {
                                    this.logger.LogInformation("Transaction {0} has received sufficient votes, it should now start getting confirmed by each peer.", agreedTransactionId);

                                    request.RequestStatus = ConversionRequestStatus.VoteFinalised;
                                }
                            }

                            break;
                        }

                    case ConversionRequestStatus.VoteFinalised:
                        {
                            BigInteger transactionId3 = this.conversionRequestCoordinationService.GetAgreedTransactionId(request.RequestId, this.interopSettings.ETHMultisigWalletQuorum);

                            if (transactionId3 != BigInteger.MinusOne)
                            {
                                // The originator isn't responsible for anything further at this point, except for periodically checking the confirmation count.
                                // The non-originators also need to monitor the confirmation count so that they know when to mark the transaction as processed locally.
                                BigInteger confirmationCount = await this.ETHClientBase.GetMultisigConfirmationCountAsync(transactionId3).ConfigureAwait(false);

                                if (confirmationCount >= this.interopSettings.ETHMultisigWalletQuorum)
                                {
                                    this.logger.LogInformation("Transaction {0} has received at least {1} confirmations, it will be automatically executed by the multisig contract.", transactionId3, this.interopSettings.ETHMultisigWalletQuorum);

                                    request.RequestStatus = ConversionRequestStatus.Processed;
                                    request.Processed = true;

                                    // We no longer need to track votes for this transaction.
                                    this.conversionRequestCoordinationService.RemoveTransaction(request.RequestId);
                                }
                                else
                                {
                                    this.logger.LogInformation("Transaction {0} has finished voting but does not yet have {1} confirmations, re-broadcasting votes to peers.", transactionId3, this.interopSettings.ETHMultisigWalletQuorum);

                                    // There are not enough confirmations yet.
                                    // Even though the vote is finalised, other nodes may come and go. So we re-broadcast the finalised votes to all federation peers.
                                    // Nodes will simply ignore the messages if they are not relevant.

                                    await this.BroadcastCoordinationAsync(request.RequestId, transactionId3).ConfigureAwait(false);

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
                            BigInteger agreedUponId = this.conversionRequestCoordinationService.GetAgreedTransactionId(request.RequestId, this.interopSettings.ETHMultisigWalletQuorum);

                            if (agreedUponId != BigInteger.MinusOne)
                            {
                                // TODO: Should we check the number of confirmations for the submission transaction here too?

                                this.logger.LogInformation("Quorum reached for conversion transaction {0} with transactionId {1}, submitting confirmation to contract.", request.RequestId, agreedUponId);

                                int gasPrice = this.externalApiPoller.GetGasPrice();

                                // If a gas price is not currently available then fall back to the value specified on the command line.
                                if (gasPrice == -1)
                                    gasPrice = this.interopSettings.ETHGasPrice;

                                this.logger.LogInformation("The non-originator will use a gas price of {0} to confirm the transaction.", gasPrice);

                                // Once a quorum is reached, each node confirms the agreed transactionId.
                                // If the originator or some other nodes renege on their vote, the current node will not re-confirm a different transactionId.
                                string confirmationHash = await this.ETHClientBase.ConfirmTransactionAsync(agreedUponId, gasPrice).ConfigureAwait(false);

                                request.RequestEthTransactionHash = confirmationHash;

                                this.logger.LogInformation("The hash of the confirmation transaction for conversion transaction {0} was {1}.", request.RequestId, confirmationHash);

                                request.RequestStatus = ConversionRequestStatus.VoteFinalised;
                            }
                            else
                            {
                                BigInteger transactionId4 = this.conversionRequestCoordinationService.GetCandidateTransactionId(request.RequestId);

                                this.logger.LogInformation("Not-Originator: Candidate transaction id for request id '{0}' is '{1}'.", request.RequestId, transactionId4);

                                if (transactionId4 != BigInteger.MinusOne)
                                {
                                    this.logger.LogInformation("Broadcasting vote (transactionId {0}) for conversion transaction {1}.", transactionId4, request.RequestId);

                                    this.conversionRequestCoordinationService.AddVote(request.RequestId, transactionId4, this.federationManager.CurrentFederationKey.PubKey);

                                    await this.BroadcastCoordinationAsync(request.RequestId, transactionId4).ConfigureAwait(false);
                                }

                                // No state transition here, as we are waiting for the candidate transactionId to progress to an agreed upon transactionId via a quorum.
                            }

                            break;
                        }
                }

                // Make sure that any state transitions are persisted to storage.
                this.conversionRequestRepository.Save(request);

                // Unlike the mint requests, burns are not initiated by the multisig wallet.
                // Instead they are initiated by the user, via a contract call to the burn() method on the WrappedStrax contract.
                // They need to provide a destination STRAX address when calling the burn method.

                // Properly processing burn transactions requires emulating a withdrawal on the main chain from the multisig wallet.
                // It will be easier when conversion can be done directly to and from a Cirrus contract instead.

                // Currently the processing is done in the WithdrawalExtractor.
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
        /// <param name="designatedMember">The federatrion member who is assigned as the originator of this conversion transaction.</param>
        /// <returns><c>true</c> if this node is selected as the originator.</returns>
        private bool DetermineConversionRequestOriginator(int blockHeight, out IFederationMember designatedMember)
        {
            // We are not able to simply use the entire federation member list, as only multisig nodes can be transaction originators.
            List<IFederationMember> federation = this.federationHistory.GetFederationForBlock(this.chainIndexer.GetHeader(blockHeight));

            var multisig = new List<CollateralFederationMember>();

            var filtered = new List<PubKey>()
            {
                new PubKey("03a37019d2e010b046ef9d0459e4844a015758007602ddfbdc9702534924a23695"),
                new PubKey("027e793fbf4f6d07de15b0aa8355f88759b8bdf92a9ffb8a65a87fa8ee03baeccd"),
                new PubKey("03e8809be396745434ee8c875089e518a3eef40e31ade81869ce9cbef63484996d"),
                new PubKey("03535a285d0919a9bd71df3b274cecb46e16b78bf50d3bf8b0a3b41028cf8a842d"),
                new PubKey("0317abe6a28cc7af44a46de97e7c6120c1ccec78afb83efe18030f5c36e3016b32"),
                new PubKey("03eb5db0b1703ea7418f0ad20582bf8de0b4105887d232c7724f43f19f14862488"),
                new PubKey("038e1a76f0e33474144b61e0796404821a5150c00b05aad8a1cd502c865d8b5b92"),
                new PubKey("0323033679aa439a0388f09f2883bf1ca6f50283b41bfeb6be6ddcc4e420144c16"),
                new PubKey("028e1d9fd64b84a2ec85fac7185deb2c87cc0dd97270cf2d8adc3aa766dde975a7"),
                new PubKey("036437789fac0ab74cda93d98b519c28608a48ef86c3bd5e8227af606c1e025f61"),
                new PubKey("03f5de5176e29e1e7d518ae76c1e020b1da18b57a3713ac81b16015026e232748e")
            };

            foreach (IFederationMember member in federation)
            {
                if (!(member is CollateralFederationMember collateralMember))
                    continue;

                if (!collateralMember.IsMultisigMember)
                    continue;

                if (this.network.NetworkType == NetworkType.Mainnet && !filtered.Contains(collateralMember.PubKey))
                    continue;

                multisig.Add(collateralMember);
            }

            // This should be impossible.
            if (multisig.Count == 0)
                throw new InteropException("There are no multisig members.");

            bool originator;

            if ((this.network.IsTest() || this.network.IsRegTest()) && this.interopSettings.OverrideOriginator)
            {
                designatedMember = this.federationManager.GetCurrentFederationMember();
                originator = true;
            }
            else
            {
                designatedMember = multisig[blockHeight % multisig.Count];
                originator = designatedMember.Equals(this.federationManager.GetCurrentFederationMember());
            }

            return originator;
        }

        /// <summary>
        /// Wait for the submission to be well-confirmed before initial vote & broadcast.
        /// </summary>
        /// <param name="identifiers">The transaction information to check.</param>
        /// <param name="caller">The caller that is waiting on the submission transaction's confirmation count.</param>
        /// <returns><c>True if it succeeded</c>, <c>false</c> if the node is stopping.</returns>
        private async Task<bool> WaitForTransactionToBeConfirmedAsync(MultisigTransactionIdentifiers identifiers, [CallerMemberName] string caller = null)
        {
            while (true)
            {
                if (this.nodeLifetime.ApplicationStopping.IsCancellationRequested)
                    return false;

                BigInteger confirmationCount = await this.ETHClientBase.GetConfirmationsAsync(identifiers.TransactionHash).ConfigureAwait(false);
                this.logger.LogInformation($"[{caller}] Waiting for the submission from the originator to be confirmed before broadcasting, transaction id {identifiers.TransactionHash}, current confirmations: {confirmationCount}.");

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
                this.logger.LogInformation("Insufficient reserve balance remaining, initiating mint transaction to replenish reserve.");

                // By minting the request amount + the reserve requirement, we cater for arbitrarily large amounts in the request.
                string mintData = this.ETHClientBase.EncodeMintParams(this.interopSettings.ETHMultisigWalletAddress, amountInWei + this.ReserveBalanceTarget);

                int gasPrice = this.externalApiPoller.GetGasPrice();

                // If a gas price is not currently available then fall back to the value specified on the command line.
                if (gasPrice == -1)
                    gasPrice = this.interopSettings.ETHGasPrice;

                this.logger.LogInformation("Originator will use a gas price of {0} to submit the mint replenishment transaction.", gasPrice);

                MultisigTransactionIdentifiers identifiers = await this.ETHClientBase.SubmitTransactionAsync(this.interopSettings.ETHWrappedStraxContractAddress, 0, mintData, gasPrice).ConfigureAwait(false);

                if (identifiers.TransactionId == BigInteger.MinusOne)
                {
                    // TODO: Submission failed, what should we do here? Note that retrying could incur additional transaction fees depending on the nature of the failure
                }
                else
                {
                    mintTransactionId = identifiers.TransactionId;

                    if (!await WaitForTransactionToBeConfirmedAsync(identifiers).ConfigureAwait(false))
                        return;
                }

                this.logger.LogInformation("Originator adding its vote for mint transaction id: {0}", mintTransactionId);

                this.conversionRequestCoordinationService.AddVote(mintRequestId, mintTransactionId, this.federationManager.CurrentFederationKey.PubKey);

                // Now we need to broadcast the mint transactionId to the other multisig nodes so that they can sign it off.
                // TODO: The other multisig nodes must be careful not to blindly trust that any given transactionId relates to a mint transaction. Need to validate the recipient
                await this.BroadcastCoordinationAsync(mintRequestId, mintTransactionId).ConfigureAwait(false);
            }
            else
                this.logger.LogInformation("Insufficient reserve balance remaining, waiting for originator to initiate mint transaction to replenish reserve.");

            BigInteger agreedTransactionId;

            // For non-originators to keep track of the ID they are intending to use.
            BigInteger ourTransactionId = BigInteger.MinusOne;

            while (true)
            {
                if (this.nodeLifetime.ApplicationStopping.IsCancellationRequested)
                    return;

                agreedTransactionId = this.conversionRequestCoordinationService.GetAgreedTransactionId(mintRequestId, this.interopSettings.ETHMultisigWalletQuorum);

                this.logger.LogDebug("Agreed transaction id '{0}'.", agreedTransactionId);

                if (agreedTransactionId != BigInteger.MinusOne)
                    break;

                // Just re-broadcast.
                if (originator)
                {
                    this.logger.LogDebug("Orignator broadcasting id {0}.", mintTransactionId);

                    await this.BroadcastCoordinationAsync(mintRequestId, mintTransactionId).ConfigureAwait(false);
                }
                else
                {
                    if (ourTransactionId == BigInteger.MinusOne)
                        ourTransactionId = this.conversionRequestCoordinationService.GetCandidateTransactionId(mintRequestId);

                    this.logger.LogDebug("Non-orignator broadcasting id {0}.", ourTransactionId);

                    if (ourTransactionId != BigInteger.MinusOne)
                    {
                        // Wait for the submission of the transaction to be confirmed on the actual chain.

                        this.conversionRequestCoordinationService.AddVote(mintRequestId, ourTransactionId, this.federationManager.CurrentFederationKey.PubKey);

                        // Broadcast our vote.
                        await this.BroadcastCoordinationAsync(mintRequestId, ourTransactionId).ConfigureAwait(false);
                    }
                }

                await Task.Delay(TimeSpan.FromSeconds(2)).ConfigureAwait(false);
            }

            this.logger.LogInformation("Agreed transaction ID for replenishment transaction: {0}", agreedTransactionId);

            if (!originator)
            {
                int gasPrice = this.externalApiPoller.GetGasPrice();

                // If a gas price is not currently available then fall back to the value specified on the command line.
                if (gasPrice == -1)
                    gasPrice = this.interopSettings.ETHGasPrice;

                this.logger.LogInformation("Non-originator will use a gas price of {0} to confirm the mint replenishment transaction.", gasPrice);

                string confirmation = await this.ETHClientBase.ConfirmTransactionAsync(agreedTransactionId, gasPrice).ConfigureAwait(false);

                this.logger.LogInformation("ID of confirmation transaction: {0}", confirmation);
            }

            while (true)
            {
                if (this.nodeLifetime.ApplicationStopping.IsCancellationRequested)
                    return;

                BigInteger confirmationCount = await this.ETHClientBase.GetMultisigConfirmationCountAsync(agreedTransactionId).ConfigureAwait(false);

                this.logger.LogInformation("Waiting for confirmation of mint replenishment transaction {0}, current count {1}.", mintRequestId, confirmationCount);

                if (confirmationCount >= this.interopSettings.ETHMultisigWalletQuorum)
                    break;

                // TODO: Maybe this should eventually age out?
                await Task.Delay(TimeSpan.FromSeconds(2)).ConfigureAwait(false);
            }

            this.logger.LogInformation("Mint replenishment transaction {0} fully confirmed.", mintTransactionId);

            while (true)
            {
                if (this.nodeLifetime.ApplicationStopping.IsCancellationRequested)
                    return;

                BigInteger balance = await this.ETHClientBase.GetErc20BalanceAsync(this.interopSettings.ETHMultisigWalletAddress).ConfigureAwait(false);

                if (balance > startBalance)
                {
                    this.logger.LogInformation("The contract's balance has been replenished, new balance {0}.", balance);
                    break;
                }
                else
                    this.logger.LogInformation("The contract's balance is unchanged at {0}.", balance);

                await Task.Delay(TimeSpan.FromSeconds(2)).ConfigureAwait(false);
            }
        }

        private async Task BroadcastCoordinationAsync(string requestId, BigInteger transactionId)
        {
            string signature = this.federationManager.CurrentFederationKey.SignMessage(requestId + ((int)transactionId));

            await this.federatedPegBroadcaster.BroadcastAsync(new InteropCoordinationPayload(requestId, (int)transactionId, signature)).ConfigureAwait(false);
        }

        private BigInteger CoinsToWei(Money coins)
        {
            BigInteger baseCurrencyUnits = Web3.Convert.ToWei(coins.ToUnit(MoneyUnit.BTC), UnitConversion.EthUnit.Ether);

            return baseCurrencyUnits;
        }

        private void AddComponentStats(StringBuilder benchLog)
        {
            benchLog.AppendLine(">> Interop Mint Requests (last 10):");

            List<ConversionRequest> requests = this.conversionRequestRepository.GetAllMint(false).OrderByDescending(i => i.BlockHeight).Take(10).ToList();
            foreach (ConversionRequest request in requests)
            {
                benchLog.AppendLine($"Id: {request.RequestId} Height: {request.BlockHeight} Destination: {request.DestinationAddress} Status: {request.RequestStatus} Amount: {request.Amount} Eth TxHash: {request.RequestEthTransactionHash}");
            }

            benchLog.AppendLine();
        }

        /// <inheritdoc />
        public void Dispose()
        {
            this.interopLoop?.Dispose();
            this.conversionLoop?.Dispose();
        }
    }
}
