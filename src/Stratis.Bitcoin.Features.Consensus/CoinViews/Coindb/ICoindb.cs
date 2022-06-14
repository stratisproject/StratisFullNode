using System.Collections.Generic;
using NBitcoin;
using Stratis.Bitcoin.Utilities;

namespace Stratis.Bitcoin.Features.Consensus.CoinViews
{
    /// <summary>
    /// Database of UTXOs.
    /// </summary>
    public interface ICoindb
    {
        /// <summary> Initialize the coin database.</summary>
        /// <param name="chainTip">The current chain's tip.</param>
        void Initialize(ChainedHeader chainTip, bool balanceIndexingEnabled);

        bool BalanceIndexingEnabled { get; }

        /// <summary>
        /// Retrieves the block hash of the current tip of the coinview.
        /// </summary>
        /// <returns>Block hash of the current tip of the coinview.</returns>
        HashHeightPair GetTipHash();

        /// <summary>
        /// Persists changes to the coinview (with the tip hash <paramref name="oldBlockHash" />) when a new block
        /// (hash <paramref name="nextBlockHash" />) is added and becomes the new tip of the coinview.
        /// <para>
        /// This method is provided (in <paramref name="unspentOutputs" /> parameter) with information about all
        /// transactions that are either new or were changed in the new block. It is also provided with information
        /// (in <see cref="originalOutputs" />) about the previous state of those transactions (if any),
        /// which is used for <see cref="Rewind" /> operation.
        /// </para>
        /// </summary>
        /// <param name="unspentOutputs">Information about the changes between the old block and the new block. An item in this list represents a list of all outputs
        /// for a specific transaction. If a specific output was spent, the output is <c>null</c>.</param>
        /// <param name="balanceUpdates">Non-cumulative balance updates at each height.</param>
        /// <param name="oldBlockHash">Block hash of the current tip of the coinview.</param>
        /// <param name="nextBlockHash">Block hash of the tip of the coinview after the change is applied.</param>
        /// <param name="rewindDataList">List of rewind data items to be persisted.</param>
        void SaveChanges(IList<UnspentOutput> unspentOutputs, Dictionary<TxDestination, Dictionary<uint, long>> balanceUpdates, HashHeightPair oldBlockHash, HashHeightPair nextBlockHash, List<RewindData> rewindDataList = null);

        /// <summary>
        /// Obtains information about unspent outputs.
        /// </summary>
        /// <param name="utxos">Transaction identifiers for which to retrieve information about unspent outputs.</param>
        /// <returns>
        /// <para>
        /// If an item of <see cref="FetchCoinsResponse.UnspentOutputs"/> is <c>null</c>, it means that outpoint is spent.
        /// </para>
        /// </returns>
        FetchCoinsResponse FetchCoins(OutPoint[] utxos);

        /// <summary>
        /// Rewinds the coinview to the last saved state.
        /// <para>
        /// This operation includes removing the UTXOs of the recent transactions
        /// and restoring recently spent outputs as UTXOs.
        /// </para>
        /// </summary>
        /// <param name="target">The final rewind target or <c>null</c> if a single block should be rewound. See remarks.</param>
        /// <returns>Hash of the block header which is now the tip of the rewound coinview.</returns>
        /// <remarks>This method can be implemented to rewind one or more blocks. Implementations
        /// that rewind only one block can ignore the target, while more advanced implementations
        /// can rewind a batch of multiple blocks but not overshooting the <paramref name="target"/>.
        /// </remarks>
        HashHeightPair Rewind(HashHeightPair target);

        /// <summary>
        /// Gets the rewind data by block height.
        /// </summary>
        /// <param name="height">The height of the block.</param>
        /// <returns>See <see cref="RewindData"/>.</returns>
        RewindData GetRewindData(int height);

        /// <summary>Gets the minimum rewind height.</summary>
        /// <returns>
        /// <para>
        /// The minimum rewind height or -1 if rewind is not possible.
        /// </para>
        /// </returns>
        int GetMinRewindHeight();

        /// <summary>
        /// Returns a combination of (height, satoshis) values with the cumulative balance up to the corresponding height.
        /// </summary>
        /// <param name="txDestination">The destination value derived from the address being queried.</param>
        /// <returns>A combination of (height, satoshis) values with the cumulative balance up to the corresponding height.</returns>
        /// <remarks>Balance updates (even when nett 0) are delivered for every height at which transactions for the address 
        /// had been recorded and as such the returned heights can be used in conjunction with the block store to discover 
        /// all related transactions.</remarks>
        IEnumerable<(uint height, long satoshis)> GetBalance(TxDestination txDestination);
    }

    public interface IStakedb : ICoindb
    {
        /// <summary>
        /// Persists unsaved POS blocks information to the database.
        /// </summary>
        /// <param name="stakeEntries">List of POS block information to be examined and persists if unsaved.</param>
        void PutStake(IEnumerable<StakeItem> stakeEntries);

        /// <summary>
        /// Retrieves POS blocks information from the database.
        /// </summary>
        /// <param name="blocklist">List of partially initialized POS block information that is to be fully initialized with the values from the database.</param>
        void GetStake(IEnumerable<StakeItem> blocklist);
    }
}
