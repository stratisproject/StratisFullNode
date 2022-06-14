﻿using System.Collections.Generic;
using NBitcoin;
using Stratis.Bitcoin.Consensus.Rules;
using Stratis.Bitcoin.Utilities;

namespace Stratis.Bitcoin.Features.Consensus.CoinViews
{

    /// <summary>
    /// Database of UTXOs.
    /// </summary>
    public interface ICoinView
    {
        /// <summary>
        /// Initializes the coin view.
        /// </summary>
        /// <param name="chainTip">The chain tip.</param>
        /// <param name="chainIndexer">The chain indexer.</param>
        /// <param name="consensusRulesContainer">The consensus rules container.</param>
        void Initialize(ChainedHeader chainTip, ChainIndexer chainIndexer, ConsensusRulesContainer consensusRulesContainer);

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
        /// <param name="oldBlockHash">Block hash of the current tip of the coinview.</param>
        /// <param name="nextBlockHash">Block hash of the tip of the coinview after the change is applied.</param>
        /// <param name="rewindDataList">List of rewind data items to be persisted. This should only be used when calling <see cref="DBreezeCoinView.SaveChanges" />.</param>
        void SaveChanges(IList<UnspentOutput> unspentOutputs, HashHeightPair oldBlockHash, HashHeightPair nextBlockHash, List<RewindData> rewindDataList = null);

        /// <summary>
        /// Brings the coinview back on-chain if a re-org occurred.
        /// </summary>
        /// <param name="chainIndexer">The current consensus chain.</param>
        void Sync(ChainIndexer chainIndexer);

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
        /// Check if given utxos are not in cache then pull them from disk and place them in to the cache
        /// </summary>
        /// <param name="utxos">Transaction output identifiers for which to retrieve information about unspent outputs.</param>
        void CacheCoins(OutPoint[] utxos);

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
        /// can rewind a batch of multiple blocks but not overshooting the <paramref name="target"/>.</remarks>
        HashHeightPair Rewind(HashHeightPair target);

        /// <summary>
        /// Gets the rewind data by block height.
        /// </summary>
        /// <param name="height">The height of the block.</param>
        RewindData GetRewindData(int height);

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
}
