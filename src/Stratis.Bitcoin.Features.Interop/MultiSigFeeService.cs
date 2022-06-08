using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
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
        Task<ReprocessFeeResult> ReprocessFeeAsync(string requestId);
        List<MultisigFeeReportItem> GenerateReport(bool onlyUnprocessed);
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

        public List<MultisigFeeReportItem> GenerateReport(bool onlyUnprocessed)
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

                if (onlyUnprocessed && deposit == null)
                    reportItems.Add(item);

                if (!onlyUnprocessed)
                    reportItems.Add(item);
            }

            return reportItems;
        }

        public async Task<ReprocessFeeResult> ReprocessFeeAsync(string requestId)
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
            ICrossChainTransfer deposit = (await this.crossChainTransferStore.GetAsync(new[] { uint256.Parse(requestId) })).FirstOrDefault();
            if (deposit != null)
            {
                if (deposit.Status == CrossChainTransferStatus.Suspended)
                {
                    // Delete the existing suspended transfer if it exists.
                    this.crossChainTransferStore.DeleteSuspendedTransfer(deposit.DepositTransactionId);
                }
                else
                {
                    var message = $"A fee deposit already exists for request '{requestId}' with state '{deposit.Status}'.";
                    this.logger.Info(message);
                    return ReprocessFeeResult.Fail(message);
                }
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

            // Inject the fee into the MaturedBlocksSyncManager again.
            this.maturedBlocksSyncManager.AddInterOpFeeDeposit(reconstructedDeposit);

            var successMessage = $"The fee associated with request '{requestId}' will be reprocessed.";
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
