using System.Collections.Generic;
using NBitcoin;
using Stratis.Bitcoin.Features.SmartContracts.MetadataTracker;

namespace Stratis.Bitcoin.Features.OpenBanking.OpenBanking
{
    public enum OpenBankDepositState
    {
        Pending = 'P',     // "Pending" deposit detected in bank account.
        Booked = 'B',      // "Booked" deposit detected in bank account.
        Minted = 'M',      // Minting transaction present in memory-pool.
        SeenInBlock = 'S'  // Minting transaction present in block.
    }

    public interface IOpenBankIdentifier
    {
        // TODO
    }

    public interface IOpenBankAccount
    {
        IOpenBankIdentifier OpenBankIdentifier { get; }

        string OpenBankAccountNumber { get; }

        MetadataTrackerEnum MetaDataTrackerEnum { get; }
    }

    public interface IOpenBankingService
    {
        /// <summary>
        /// Returns the bank deposits for a given state in descending order by ExternalId.
        /// </summary>
        /// <param name="bankAccountIdentifier">The bank account to return the deposits for.</param>
        /// <param name="state">The deposit state filter.</param>
        /// <returns>The bank deposits for a given state in descending order by ExternalId.</returns>
        IEnumerable<OpenBankDeposit> GetOpenBankDeposits(IOpenBankAccount bankAccountIdentifier, OpenBankDepositState state);

        OpenBankDeposit GetOpenBankDeposit(IOpenBankAccount openBankAccount, string externalId);

        void UpdateDeposits(IOpenBankAccount openBankAccount);

        void UpdateDepositStatus(IOpenBankAccount openBankAccount);

        void SetTransactionId(IOpenBankAccount openBankAccount, OpenBankDeposit deposit, uint256 txId);
    }
}
