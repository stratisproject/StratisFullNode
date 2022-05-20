using System.Collections.Generic;
using System.Linq;
using NBitcoin;
using Newtonsoft.Json;
using NLog;
using Stratis.Features.FederatedPeg.Conversion;
using Stratis.Features.FederatedPeg.Interfaces;
using Stratis.Features.FederatedPeg.SourceChain;
using Stratis.Features.FederatedPeg.TargetChain;

namespace Stratis.Bitcoin.Features.Interop
{
    public interface IMultiSigFeeService
    {
        ReprocessFeeResult ReprocessFee(string requestId);
        List<MultisigFeeReportItem> GenerateReport();
    }

    public sealed class MultiSigFeeService : IMultiSigFeeService
    {
        private readonly ChainIndexer chainIndexer;
        private readonly IConversionRequestRepository conversionRequestRepository;
        private readonly ICrossChainTransferStore crossChainTransferStore;
        private readonly ILogger logger;
        private readonly IMaturedBlocksSyncManager maturedBlocksSyncManager;
        private readonly Network network;

        public MultiSigFeeService(
            ChainIndexer chainIndexer,
            IConversionRequestRepository conversionRequestRepository,
            ICrossChainTransferStore crossChainTransferStore,
            IMaturedBlocksSyncManager maturedBlocksSyncManager,
            Network network)
        {
            this.chainIndexer = chainIndexer;
            this.conversionRequestRepository = conversionRequestRepository;
            this.crossChainTransferStore = crossChainTransferStore;
            this.logger = LogManager.GetCurrentClassLogger();
            this.maturedBlocksSyncManager = maturedBlocksSyncManager;
            this.network = network;
        }

        public List<MultisigFeeReportItem> GenerateReport()
        {
            // Find all processed burn requests.
            List<ConversionRequest> burns = this.conversionRequestRepository.GetAllBurn(false);

            ICrossChainTransfer[] deposits = this.crossChainTransferStore.GetTransfersByStatus(new[] { CrossChainTransferStatus.Suspended, CrossChainTransferStatus.SeenInBlock });

            var reportItems = new List<MultisigFeeReportItem>();
            foreach (ConversionRequest burn in burns)
            {
                var item = new MultisigFeeReportItem()
                {
                    RequestBlockHeight = burn.BlockHeight,
                    RequestId = burn.RequestId
                };

                ICrossChainTransfer deposit = deposits.FirstOrDefault(d => d.DepositTransactionId == uint256.Parse(burn.RequestId));
                if (deposit != null)
                {
                    item.ExistInStore = true;
                    item.DepositBlockHeight = deposit.BlockHeight;
                    item.AmountInStore = deposit.DepositAmount;
                    item.FeeDepositState = deposit.Status.ToString();
                }

                reportItems.Add(item);
            }

            return reportItems;
        }

        public ReprocessFeeResult ReprocessFee(string requestId)
        {
            // First check if the request exists and is processed.
            ConversionRequest conversionRequest = this.conversionRequestRepository.Get(requestId);
            if (conversionRequest == null)
            {
                var message = $"Request '{requestId}' does not exist.";
                this.logger.Info(message);
                return ReprocessFeeResult.Fail(message);
            }

            if (conversionRequest.RequestStatus != ConversionRequestStatus.Processed)
            {
                var message = $"Request '{requestId}' is not processed, current state '{conversionRequest.RequestStatus}'.";
                this.logger.Info(message);
                return ReprocessFeeResult.Fail(message);
            }

            // First check if the request id exists as a suspended deposit in the cross chain transfer store
            ICrossChainTransfer deposit = this.crossChainTransferStore.GetTransfersByStatus(new[] { CrossChainTransferStatus.Suspended }).FirstOrDefault(s => s.DepositTransactionId == uint256.Parse(requestId));
            if (deposit == null)
            {
                var message = $"A request with id '{requestId}' does not exist as a suspended deposit transaction.";
                this.logger.Info(message);
                return ReprocessFeeResult.Fail(message);
            }

            // Construct a new deposit object from the existing one.
            var reconstructedDeposit = new Deposit(
                                deposit.DepositTransactionId,
                                DepositRetrievalType.Distribution,
                                Money.Satoshis(deposit.DepositAmount),
                                this.network.ConversionTransactionFeeDistributionDummyAddress,
                                conversionRequest.DestinationChain,
                                conversionRequest.BlockHeight,
                                this.chainIndexer.GetHeader(conversionRequest.BlockHeight).HashBlock
                               );

            // Delete the existing suspended transfer.
            this.crossChainTransferStore.DeleteSuspendedTransfer(deposit.DepositTransactionId);

            // Inject the fee into the MaturedBlocksSyncManager again.
            this.maturedBlocksSyncManager.AddInterOpFeeDeposit(reconstructedDeposit);

            var successMessage = $"The fee associated to request '{requestId}' will be reprocessed.";
            this.logger.Info(successMessage);

            return ReprocessFeeResult.Success(successMessage);
        }
    }

    public sealed class ReprocessFeeResult
    {
        public bool Succeeded { get; set; }
        public string Message { get; private set; }

        public static ReprocessFeeResult Fail(string message)
        {
            return new ReprocessFeeResult() { Message = message };
        }

        public static ReprocessFeeResult Success(string message)
        {
            return new ReprocessFeeResult() { Succeeded = true, Message = message };
        }
    }

    public sealed class MultisigFeeReportItem
    {
        [JsonProperty("requestId")]
        public string RequestId { get; set; }

        [JsonProperty("existInStore")]
        public bool ExistInStore { get; set; }

        [JsonProperty("amountInStore")]
        public decimal AmountInStore { get; set; }

        [JsonProperty("feeDepositState")]
        public string FeeDepositState { get; set; }

        [JsonProperty("depositBlockHeight")]
        public int? DepositBlockHeight { get; set; }

        [JsonProperty("requestBlockHeight")]
        public int RequestBlockHeight { get; set; }
    }
}
