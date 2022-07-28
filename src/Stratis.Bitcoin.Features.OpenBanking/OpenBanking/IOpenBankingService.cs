using System;
using System.Collections.Generic;
using NBitcoin;
using Stratis.Bitcoin.Features.SmartContracts.MetadataTracker;

namespace Stratis.Bitcoin.Features.OpenBanking.OpenBanking
{
    /// <summary>
    /// Represents the state of an open banking deposit.
    /// </summary>
    public enum OpenBankDepositState
    {
        Error = 'E',       // Deposit is 'Booked' but has an invalid reference.
        Pending = 'P',     // "Pending" deposit detected in bank account.
        Booked = 'B',      // "Booked" deposit detected in bank account.
        Minted = 'M',      // Minting transaction present in memory-pool.
        SeenInBlock = 'S'  // Minting transaction present in block.
    }

    /// <summary>
    /// Holds open banking connection details.
    /// </summary>
    public class OpenBankConfiguration
    {
        // E.g. "https://localhost:44315/"
        public string RedirectURL { get; set; }

        // E.g. "https://ob-mtls-resource-server.azurewebsites.net/token"
        public string TokenURL { get; set; }

        // E.g. "https://ob-mtls-resource-server.azurewebsites.net/auth/code"
        public string AuthCodeURL { get; set; }

        // E.g. "https://ob-mtls-resource-server.azurewebsites.net/open-banking/v3.1/aisp"
        public string AISPURL { get; set; }
    }

    public static class IOpenBankAccountExt
    {
        public static string TransactionsEndpoint(this IOpenBankAccount openBankAccount, DateTime? fromDateTime)
        {
            return $"{openBankAccount.OpenBankConfiguration.AISPURL}/accounts/{openBankAccount.OpenBankAccountNumber}/transactions";
        }
    }

    /// <summary>
    /// Holds information about an open banking account to track deposits for.
    /// </summary>
    public interface IOpenBankAccount
    {
        /// <summary>
        /// Connection details for the open banking account.
        /// </summary>
        OpenBankConfiguration OpenBankConfiguration { get; }

        /// <summary>
        /// The open banking account number.
        /// </summary>
        string OpenBankAccountNumber { get; }

        /// <summary>
        /// Identifies the tracking table to use.
        /// </summary>
        MetadataTableNumber MetaDataTable { get; }

        /// <summary>
        /// The expected open banking currency associated with deposit amounts.
        /// </summary>
        string Currency { get; }

        /// <summary>
        /// Identifies the stablecoin contract that ties up with the bank account.
        /// </summary>
        string Contract { get; }

        /// <summary>
        /// Identifies the first block to monitor to confirm stable coin minting calls.
        /// </summary>
        int FirstBlock { get; }
    }

    public interface IOpenBankingService
    {
        /// <summary>
        /// Returns the bank deposits for a given state in descending order by ExternalId.
        /// </summary>
        /// <param name="openBankAccount">The bank account to return the deposits for.</param>
        /// <param name="state">The deposit state filter.</param>
        /// <returns>The bank deposits for a given state in descending order by ExternalId.</returns>
        IEnumerable<OpenBankDeposit> GetOpenBankDeposits(IOpenBankAccount openBankAccount, OpenBankDepositState state);

        /// <summary>
        /// Returns a bank deposit identified by key.
        /// </summary>
        /// <param name="openBankAccount">The bank account to return the deposits for.</param>
        /// <param name="keyBytes">The key bytes. See <see cref="OpenBankDeposit.KeyBytes"/>.</param>
        /// <returns>See <see cref="OpenBankDeposit"/>.</returns>
        OpenBankDeposit GetOpenBankDeposit(IOpenBankAccount openBankAccount, byte[] keyBytes);

        /// <summary>
        /// Synchronizes the local deposits with deposits obtained via the Open Banking API.
        /// </summary>
        /// <param name="openBankAccount">The bank account for which to update the information.</param>
        void UpdateDeposits(IOpenBankAccount openBankAccount);

        /// <summary>
        /// Updates our local deposit status depending on what is found in the memory pool or chain. 
        /// </summary>
        /// <param name="openBankAccount">The bank account for which to update the information.</param>
        void UpdateDepositStatus(IOpenBankAccount openBankAccount);

        /// <summary>
        /// Updates the deposit to contain the transaction id of the minting call.
        /// </summary>
        /// <param name="openBankAccount">The bank account for which to update the information.</param>
        /// <param name="deposit">The deposit to update.</param>
        /// <param name="txId">The transaction id of the minting call.</param>
        void SetTransactionId(IOpenBankAccount openBankAccount, OpenBankDeposit deposit, uint256 txId);
    }
}
