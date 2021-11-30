using System.Collections.Generic;
using Stratis.Bitcoin.Features.SmartContracts.Models;
using Stratis.SmartContracts.CLR;

namespace Stratis.Bitcoin.Features.SmartContracts.Wallet
{
    public interface ISmartContractTransactionService
    {
        EstimateFeeResult EstimateFee(ScTxFeeEstimateRequest request);

        BuildContractTransactionResult BuildTx(BuildContractTransactionRequest request);

        BuildCallContractTransactionResponse BuildCallTx(BuildCallContractTransactionRequest request);

        BuildCreateContractTransactionResponse BuildCreateTx(BuildCreateContractTransactionRequest request);

        ContractTxData BuildLocalCallTxData(LocalCallContractRequest request);

        /// <summary>
        /// Searches for receipts that match the given filter criteria. Filter criteria are ANDed together.
        /// </summary>
        /// <param name="contractAddress">The contract address from which events were raised.</param>
        /// <param name="eventName">The name of the event raised.</param>
        /// <param name="topics">The topics to search. All specified topics must be present.</param>
        /// <param name="fromBlock">The block number from which to start searching.</param>
        /// <param name="toBlock">The block number where searching finishes.</param>
        /// <returns>A list of all matching receipts.</returns>
        List<ReceiptResponse> ReceiptSearch(string contractAddress, string eventName, List<string> topics = null, int fromBlock = 0, int? toBlock = null);
    }
}