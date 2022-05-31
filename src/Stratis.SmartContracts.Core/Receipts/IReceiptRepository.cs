using System.Collections.Generic;
using NBitcoin;

namespace Stratis.SmartContracts.Core.Receipts
{
    public interface IReceiptRepository
    {
        /// <summary>
        /// Permanently store several receipts.
        /// </summary>
        /// <param name="receipts">Receipts to store.</param>
        void Store(IEnumerable<Receipt> receipts);

        /// <summary>
        /// Retrieve a receipt by transaction hash.
        /// </summary>
        /// <param name="txHash">Hash of transaction to retrieve.</param>
        /// <returns><see cref="Receipt"/>.</returns>
        Receipt Retrieve(uint256 txHash);

        /// <summary>
        /// Retrieves the receipt for each of the given IDs. It will put null in an index
        /// if that hash is not found in the database.
        /// </summary>
        /// <param name="hashes">Hashes for which to retrieve receipts.</param>
        /// <returns>List of receipts retrieved or <c>null</c> for each hash not found.</returns>
        IList<Receipt> RetrieveMany(IList<uint256> hashes);
    }
}
