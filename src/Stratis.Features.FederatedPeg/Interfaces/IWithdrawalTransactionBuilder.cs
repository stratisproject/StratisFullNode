using NBitcoin;
using Stratis.Features.FederatedPeg.Wallet;

namespace Stratis.Features.FederatedPeg.Interfaces
{
    public interface IWithdrawalTransactionBuilder
    {
        /// <summary>
        /// Builds a transaction withdrawing funds from the federation to a given address, presumably because of a deposit transaction on the other chain.
        /// </summary>
        /// <param name="blockHeight">The height of the block on the counterchain that contained the deposit transaction.</param>
        /// <param name="depositId">Hash of the deposit transaction seen.</param>
        /// <param name="blockTime">Used to sign transactions in the case of PoS.</param>
        /// <param name="recipient">The address to receive the withdrawal funds.</param>
        /// <returns>The transaction ready to be signed.</returns>
        Transaction BuildWithdrawalTransaction(int blockHeight, uint256 depositId, uint blockTime, Recipient recipient);
    }
}
