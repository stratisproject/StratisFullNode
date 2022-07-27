using NBitcoin;
using Stratis.Bitcoin.Features.OpenBanking.OpenBanking;

namespace Stratis.Bitcoin.Features.OpenBanking.TokenMinter
{
    /// <summary>
    /// Transaction builder for the minting call.
    /// </summary>
    public interface ITokenMintingTransactionBuilder
    {
        /// <summary>
        /// Creates a transaction that calls the minting method for the stablecoin when
        /// provided with the bank account details and the deposit.
        /// </summary>
        /// <param name="openBankAccount">The bank account.</param>
        /// <param name="openBankDeposit">The deposit.</param>
        /// <returns>A transaction that calls the stable coin's minting method.</returns>
        Transaction BuildSignedTransaction(IOpenBankAccount openBankAccount, OpenBankDeposit openBankDeposit);
    }
}
