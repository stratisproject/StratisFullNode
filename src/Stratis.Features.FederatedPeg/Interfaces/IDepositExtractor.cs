using System.Collections.Generic;
using System.Threading.Tasks;
using NBitcoin;
using Stratis.Features.FederatedPeg.SourceChain;

namespace Stratis.Features.FederatedPeg.Interfaces
{
    /// <summary>
    /// This component is responsible for finding all deposits made to a given address
    /// in a given block, find out if they should trigger a cross chain transfer, and if so
    /// extracting the transfers' details.
    /// </summary>
    public interface IDepositExtractor
    {
        /// <summary>
        /// Extracts deposits from the transactions in a block.
        /// </summary>
        /// <param name="block">The block containing the transactions.</param>
        /// <param name="blockHeight">The height of the block containing the transactions.</param>
        /// <param name="depositRetrievalTypes">The types of retrieval to perform. The type will determine the value of the deposit to be processed.</param>
        /// <returns>The extracted deposit (if any), otherwise returns an empty list.</returns>
        Task<IReadOnlyList<IDeposit>> ExtractDepositsFromBlock(Block block, int blockHeight, Dictionary<DepositRetrievalType, int> depositRetrievalTypes);

        /// <summary>
        /// Extracts a deposit from a transaction (if any).
        /// </summary>
        /// <param name="transaction">The transaction to extract deposits from.</param>
        /// <param name="blockHeight">The block height of the block containing the transaction.</param>
        /// <param name="blockHash">The block hash of the block containing the transaction.</param>
        /// <returns>The extracted deposit (if any), otherwise <c>null</c>.</returns>
        Task<IDeposit> ExtractDepositFromTransaction(Transaction transaction, int blockHeight, uint256 blockHash);
    }
}
