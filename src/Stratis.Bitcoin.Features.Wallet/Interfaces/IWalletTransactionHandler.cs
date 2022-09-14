using NBitcoin;

namespace Stratis.Bitcoin.Features.Wallet.Interfaces
{
    /// <summary>
    /// A handler that has various functionalities related to transaction operations.
    /// </summary>
    public interface IWalletTransactionHandler
    {
        /// <summary>
        /// Builds a new transaction based on information from the <see cref="TransactionBuildContext"/>.
        /// </summary>
        /// <param name="context">The context that is used to build a new transaction.</param>
        /// <returns>The new transaction.</returns>
        Transaction BuildTransaction(TransactionBuildContext context);

        /// <summary>
        /// Calculates the maximum amount a user can spend in a single transaction, taking into account the fees required.
        /// </summary>
        /// <param name="accountReference">The account from which to calculate the amount.</param>
        /// <param name="feeType">The type of fee used to calculate the maximum amount the user can spend. The higher the fee, the smaller this amount will be.</param>
        /// <param name="allowUnconfirmed"><c>true</c> to include unconfirmed transactions in the calculation, <c>false</c> otherwise.</param>
        /// <returns>The maximum amount the user can spend in a single transaction, along with the fee required.</returns>
        (Money maximumSpendableAmount, Money Fee) GetMaximumSpendableAmount(WalletAccountReference accountReference, FeeType feeType, bool allowUnconfirmed);

        /// <summary>
        /// Estimates the fee for the transaction based on information from the <see cref="TransactionBuildContext"/>.
        /// </summary>
        /// <param name="context">The context that is used to build a new transaction.</param>
        /// <returns>The estimated fee.</returns>
        Money EstimateFee(TransactionBuildContext context);

        int EstimateSize(TransactionBuildContext context);
    }
}
