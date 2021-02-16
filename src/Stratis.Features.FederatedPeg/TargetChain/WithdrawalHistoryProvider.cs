using System.Collections.Generic;
using System.Linq;
using NBitcoin;
using Stratis.Bitcoin.Features.MemoryPool;
using Stratis.Features.Collateral.CounterChain;
using Stratis.Features.FederatedPeg.Interfaces;
using Stratis.Features.FederatedPeg.Models;

namespace Stratis.Features.FederatedPeg.TargetChain
{
    public interface IWithdrawalHistoryProvider
    {
        List<WithdrawalModel> GetHistory(IEnumerable<ICrossChainTransfer> crossChainTransfers, int maximumEntriesToReturn);
        List<WithdrawalModel> GetPendingWithdrawals(IEnumerable<ICrossChainTransfer> crossChainTransfers);
    }

    public class WithdrawalHistoryProvider : IWithdrawalHistoryProvider
    {
        private readonly IFederatedPegSettings federatedPegSettings;
        private readonly MempoolManager mempoolManager;
        private readonly Network network;
        private readonly IWithdrawalExtractor withdrawalExtractor;

        /// <summary>
        /// The <see cref="WithdrawalHistoryProvider"/> constructor.
        /// </summary>
        /// <param name="network">Network we are running on.</param>
        /// <param name="federatedPegSettings">Federation settings providing access to number of signatures required.</param>
        /// <param name="mempoolManager">Mempool which provides information about transactions in the mempool.</param>
        /// <param name="counterChainNetworkWrapper">Counter chain network.</param>
        public WithdrawalHistoryProvider(
            Network network,
            IFederatedPegSettings federatedPegSettings,
            MempoolManager mempoolManager,
            CounterChainNetworkWrapper counterChainNetworkWrapper)
        {
            this.network = network;
            this.federatedPegSettings = federatedPegSettings;
            this.withdrawalExtractor = new WithdrawalExtractor(federatedPegSettings, new OpReturnDataReader(counterChainNetworkWrapper), network);
            this.mempoolManager = mempoolManager;
        }

        /// <summary>
        /// Get the history of successful withdrawals.
        /// </summary>
        /// <param name="crossChainTransfers">The list of transfers to report on.</param>
        /// <param name="maximumEntriesToReturn">The maximum number of entries to return.</param>
        /// <returns>A <see cref="WithdrawalModel"/> object containing a history of withdrawals.</returns>
        public List<WithdrawalModel> GetHistory(IEnumerable<ICrossChainTransfer> crossChainTransfers, int maximumEntriesToReturn)
        {
            var result = new List<WithdrawalModel>();

            foreach (ICrossChainTransfer transfer in crossChainTransfers.OrderByDescending(t => t.BlockHeight))
            {
                if (maximumEntriesToReturn-- <= 0)
                    break;

                // Extract the withdrawal details from the recorded "PartialTransaction".
                IWithdrawal withdrawal = this.withdrawalExtractor.ExtractWithdrawalFromTransaction(transfer.PartialTransaction, transfer.BlockHash, (int)transfer.BlockHeight);
                var model = new WithdrawalModel(this.network, withdrawal, transfer);
                result.Add(model);
            }

            return result;
        }

        /// <summary>
        /// Get pending withdrawals.
        /// </summary>
        /// <param name="crossChainTransfers">The list of transfers to report on.</param>
        /// <returns>A <see cref="WithdrawalModel"/> object containing pending withdrawals and statuses.</returns>
        public List<WithdrawalModel> GetPendingWithdrawals(IEnumerable<ICrossChainTransfer> crossChainTransfers)
        {
            var result = new List<WithdrawalModel>();

            foreach (ICrossChainTransfer transfer in crossChainTransfers)
            {
                var model = new WithdrawalModel(this.network, transfer);
                string status = transfer?.Status.ToString();
                switch (transfer?.Status)
                {
                    case CrossChainTransferStatus.FullySigned:
                        if (this.mempoolManager.InfoAsync(model.Id).GetAwaiter().GetResult() != null)
                            status += "+InMempool";

                        model.SpendingOutputDetails = this.GetSpendingInfo(transfer.PartialTransaction);
                        break;
                    case CrossChainTransferStatus.Partial:
                        model.SignatureCount = transfer.GetSignatureCount(this.network);
                        status += " (" + model.SignatureCount + "/" + this.federatedPegSettings.MultiSigM + ")";
                        model.SpendingOutputDetails = this.GetSpendingInfo(transfer.PartialTransaction);
                        break;
                }

                model.TransferStatus = status;

                result.Add(model);
            }

            return result;
        }

        private string GetSpendingInfo(Transaction partialTransaction)
        {
            string ret = "";

            foreach (TxIn input in partialTransaction.Inputs)
            {
                ret += input.PrevOut.Hash.ToString().Substring(0, 6) + "-" + input.PrevOut.N + ",";
            }

            return ret;
        }
    }
}
