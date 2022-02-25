using System.Numerics;
using System.Threading.Tasks;
using Stratis.Bitcoin.Features.Interop.ETHClient;
using Stratis.Features.FederatedPeg.Conversion;
using NLog;
using Stratis.Bitcoin.Features.ExternalApi;
using Stratis.Bitcoin.Features.Interop.Settings;
using Stratis.Bitcoin.Features.PoA;
using Stratis.Bitcoin.Features.Wallet;
using Stratis.Features.FederatedPeg.Coordination;
using Stratis.Bitcoin.Features.Interop.Payloads;
using Stratis.Features.FederatedPeg.Interfaces;

namespace Stratis.Bitcoin.Features.Interop
{
    /// <summary>
    /// Holds the implementations of methods needed by the <see cref="InteropPoller"/>
    /// </summary>
    public class InteropPollerStateMachine
    {
        private readonly ILogger logger;
        private readonly IExternalApiPoller externalApiPoller;
        private readonly IConversionRequestCoordinationService conversionRequestCoordinationService;
        private readonly IFederationManager federationManager;
        private readonly IFederatedPegBroadcaster federatedPegBroadcaster;

        public InteropPollerStateMachine(ILogger logger, IExternalApiPoller externalApiPoller, IConversionRequestCoordinationService conversionRequestCoordinationService, IFederationManager federationManager, IFederatedPegBroadcaster federatedPegBroadcaster)
        {
            this.logger = logger;
            this.externalApiPoller = externalApiPoller;
            this.conversionRequestCoordinationService = conversionRequestCoordinationService;
            this.federationManager = federationManager;
            this.federatedPegBroadcaster = federatedPegBroadcaster;
        }

        public async Task UnprocessedAsync(ConversionRequest request, bool originator, IFederationMember designatedMember)
        {
            string transactionType = request.RequestType == ConversionRequestType.Burn ? "SRC20->ERC20" : "CRS->WSTRAX";

            if (originator)
            {
                // If this node is the designated transaction originator, it must create and submit the transaction to the multisig.
                this.logger.Info($"This node selected as originator for {transactionType} transaction '{request.RequestId}'.");

                request.RequestStatus = ConversionRequestStatus.OriginatorNotSubmitted;
            }
            else
            {
                this.logger.Info($"This node was not selected as the originator for {transactionType} transaction '{request.RequestId}'. The originator is: '{(designatedMember == null ? "N/A (Overridden)" : designatedMember.PubKey?.ToHex())}'.");

                request.RequestStatus = ConversionRequestStatus.NotOriginator;
            }
        }

        public async Task OriginatorNotSubmittedAsync(ConversionRequest request, IETHClient clientForDestChain, InteropSettings interopSettings)
        {
            string transactionType = request.RequestType == ConversionRequestType.Burn ? "SRC20->ERC20" : "CRS->WSTRAX";

            this.logger.Info($"{transactionType} conversion not yet submitted, checking which gas price to use.");

            // First construct the necessary transfer() transaction data, utilising the ABI of a standard ERC20 contract.
            // When this constructed transaction is actually executed, the transfer's source account will be the account executing the transaction i.e. the multisig contract address.
            string abiData = clientForDestChain.EncodeTransferParams(request.DestinationAddress, new BigInteger(request.Amount.ToBytes()));

            int gasPrice = this.externalApiPoller.GetGasPrice();

            // If a gas price is not currently available then fall back to the value specified on the command line.
            if (gasPrice == -1)
                gasPrice = interopSettings.GetSettingsByChain(request.DestinationChain).GasPrice;

            this.logger.Info($"Originator will use a gas price of {gasPrice} to submit the {transactionType} transaction.");

            // Submit the unconfirmed transaction data to the multisig contract, returning a transactionId used to refer to it.
            // Once sufficient multisig owners have confirmed the transaction the multisig contract will execute it.
            // Note that by submitting the transaction to the multisig wallet contract, the originator is implicitly granting it one confirmation.
            MultisigTransactionIdentifiers identifiers = await clientForDestChain.SubmitTransactionAsync(request.TokenContract, 0, abiData, gasPrice).ConfigureAwait(false);

            if (identifiers.TransactionId == BigInteger.MinusOne)
            {
                this.logger.Error($"{transactionType} conversion on {request.DestinationChain} to address '{request.DestinationAddress}' for {request.Amount} failed: {identifiers.Message}");

                request.Processed = true;
                request.RequestStatus = ConversionRequestStatus.Failed;
                request.StatusMessage = identifiers.Message;

                // TODO: Submitting the transaction failed, this needs to be handled
            }
            else
                request.RequestStatus = ConversionRequestStatus.OriginatorSubmitting;

            request.ExternalChainBlockHeight = identifiers.BlockHeight;
            request.ExternalChainTxHash = identifiers.TransactionHash;
            request.ExternalChainTxEventId = identifiers.TransactionId.ToString();
        }

        public async Task OriginatorSubmittingAsync(ConversionRequest request, IETHClient clientForDestChain, BigInteger submissionConfirmationThreshold)
        {
            string transactionType = request.RequestType == ConversionRequestType.Burn ? "SRC20->ERC20" : "CRS->WSTRAX";

            (BigInteger confirmationCount, string blockHash) = await clientForDestChain.GetConfirmationsAsync(request.ExternalChainTxHash).ConfigureAwait(false);

            this.logger.Info($"Originator confirming {transactionType} transaction id '{request.ExternalChainTxHash}' '({request.ExternalChainTxEventId})' before broadcasting; confirmations: {confirmationCount}; Block Hash {blockHash}.");

            if (confirmationCount < submissionConfirmationThreshold)
                return;

            this.logger.Info($"Originator submitted {transactionType} transaction to multisig in transaction '{request.ExternalChainTxHash}' and was allocated transactionId '{request.ExternalChainTxEventId}'.");

            this.conversionRequestCoordinationService.AddVote(request.RequestId, BigInteger.Parse(request.ExternalChainTxEventId), this.federationManager.CurrentFederationKey.PubKey);

            request.RequestStatus = ConversionRequestStatus.OriginatorSubmitted;
        }

        public async Task OriginatorSubmittedAsync(ConversionRequest request, InteropSettings interopSettings)
        {
            string transactionType = request.RequestType == ConversionRequestType.Burn ? "SRC20->ERC20" : "CRS->WSTRAX";

            BigInteger transactionId2 = this.conversionRequestCoordinationService.GetCandidateTransactionId(request.RequestId);

            if (transactionId2 != BigInteger.MinusOne)
            {
                await this.BroadcastCoordinationVoteRequestAsync(request.RequestId, transactionId2, request.DestinationChain, false).ConfigureAwait(false);

                BigInteger agreedTransactionId = this.conversionRequestCoordinationService.GetAgreedTransactionId(request.RequestId, interopSettings.GetSettingsByChain(request.DestinationChain).MultisigWalletQuorum);

                if (agreedTransactionId != BigInteger.MinusOne)
                {
                    this.logger.Info($"{transactionType} transaction '{agreedTransactionId}' has received sufficient votes, it should now start getting confirmed by each peer.");

                    request.RequestStatus = ConversionRequestStatus.VoteFinalised;
                }
            }
        }

        public async Task VoteFinalisedAsync(ConversionRequest request, IETHClient clientForDestChain, InteropSettings interopSettings)
        {
            string transactionType = request.RequestType == ConversionRequestType.Burn ? "SRC20->ERC20" : "CRS->WSTRAX";

            BigInteger transactionId3 = this.conversionRequestCoordinationService.GetAgreedTransactionId(request.RequestId, interopSettings.GetSettingsByChain(request.DestinationChain).MultisigWalletQuorum);

            if (transactionId3 != BigInteger.MinusOne)
            {
                // The originator isn't responsible for anything further at this point, except for periodically checking the confirmation count.
                // The non-originators also need to monitor the confirmation count so that they know when to mark the transaction as processed locally.
                BigInteger confirmationCount = await clientForDestChain.GetMultisigConfirmationCountAsync(transactionId3).ConfigureAwait(false);

                if (confirmationCount >= interopSettings.GetSettingsByChain(request.DestinationChain).MultisigWalletQuorum)
                {
                    this.logger.Info($"{transactionType} transaction '{transactionId3}' has received at least {interopSettings.GetSettingsByChain(request.DestinationChain).MultisigWalletQuorum} confirmations, it will be automatically executed by the multisig contract.");

                    request.RequestStatus = ConversionRequestStatus.Processed;
                    request.Processed = true;

                    // We no longer need to track votes for this transaction.
                    this.conversionRequestCoordinationService.RemoveTransaction(request.RequestId);
                }
                else
                {
                    this.logger.Info("Transaction '{0}' has finished voting but does not yet have {1} confirmations, re-broadcasting votes to peers.", transactionId3, interopSettings.GetSettingsByChain(request.DestinationChain).MultisigWalletQuorum);
                    
                    // There are not enough confirmations yet.
                    // Even though the vote is finalised, other nodes may come and go. So we re-broadcast the finalised votes to all federation peers.
                    // Nodes will simply ignore the messages if they are not relevant.
                    await this.BroadcastCoordinationVoteRequestAsync(request.RequestId, transactionId3, request.DestinationChain, false).ConfigureAwait(false);

                    // No state transition here, we are waiting for sufficient confirmations.
                }
            }
        }

        public async Task NotOriginatorAsync(ConversionRequest request, IETHClient clientForDestChain, InteropSettings interopSettings)
        {
            string transactionType = request.RequestType == ConversionRequestType.Burn ? "SRC20->ERC20" : "CRS->WSTRAX";

            BigInteger agreedUponId = this.conversionRequestCoordinationService.GetAgreedTransactionId(request.RequestId, interopSettings.GetSettingsByChain(request.DestinationChain).MultisigWalletQuorum);

            if (agreedUponId != BigInteger.MinusOne)
            {
                // TODO: Should we check the number of confirmations for the submission transaction here too?

                this.logger.Info($"Quorum reached for {transactionType} conversion transaction '{request.RequestId}' with transactionId '{agreedUponId}', submitting confirmation to contract.");

                int gasPrice = this.externalApiPoller.GetGasPrice();

                // If a gas price is not currently available then fall back to the value specified on the command line.
                if (gasPrice == -1)
                    gasPrice = interopSettings.GetSettingsByChain(request.DestinationChain).GasPrice;

                this.logger.Info("The non-originator will use a gas price of {0} to confirm the transaction.", gasPrice);

                // Once a quorum is reached, each node confirms the agreed transactionId.
                // If the originator or some other nodes renege on their vote, the current node will not re-confirm a different transactionId.
                string confirmationHash = await clientForDestChain.ConfirmTransactionAsync(agreedUponId, gasPrice).ConfigureAwait(false);

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

                    await this.BroadcastCoordinationVoteRequestAsync(request.RequestId, transactionId4, request.DestinationChain, false).ConfigureAwait(false);
                }

                // No state transition here, as we are waiting for the candidate transactionId to progress to an agreed upon transactionId via a quorum.
            }
        }

        private async Task BroadcastCoordinationVoteRequestAsync(string requestId, BigInteger transactionId, DestinationChain destinationChain, bool isTransfer)
        {
            string signature = this.federationManager.CurrentFederationKey.SignMessage(requestId + ((int)transactionId));
            await this.federatedPegBroadcaster.BroadcastAsync(ConversionRequestPayload.Request(requestId, (int)transactionId, signature, destinationChain, isTransfer)).ConfigureAwait(false);
        }
    }
}
