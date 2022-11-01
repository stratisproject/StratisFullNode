﻿using System;
using System.Collections.Generic;
using System.Threading;
using NBitcoin;

namespace Stratis.Bitcoin.Interfaces
{
    /// <summary>
    /// Represent access to the store of <see cref="Block"/>.
    /// </summary>
    public interface IBlockStore : IDisposable
    {
        /// <summary>
        /// Initializes the blockchain storage and ensure the genesis block has been created in the database.
        /// </summary>
        void Initialize();

        /// <summary>Retrieve the transaction information asynchronously using transaction id.</summary>
        /// <param name="trxid">The transaction id to find.</param>
        Transaction GetTransactionById(uint256 trxid);

        /// <summary>Retrieve transactions information asynchronously using transaction ids.</summary>
        /// <param name="trxids">Ids of transactions to find.</param>
        /// <returns>List of transactions or <c>null</c> if txindexing is disabled.</returns>
        Transaction[] GetTransactionsByIds(uint256[] trxids, CancellationToken cancellation = default(CancellationToken));

        /// <summary>
        /// Get the corresponding block hash by using transaction hash.
        /// </summary>
        /// <param name="trxid">The transaction hash.</param>
        uint256 GetBlockIdByTransactionId(uint256 trxid);

        /// <summary>
        /// Get the block from the database by using block hash.
        /// </summary>
        /// <param name="blockHash">The block hash.</param>
        Block GetBlock(uint256 blockHash);

        List<Block> GetBlocks(List<uint256> blockHashes);

        /// <summary> Indicates that the node should store all transaction data in the database.</summary>
        bool TxIndex { get; }
    }

    /// <summary>
    /// Provides functionality that builds upon the <see cref="IBlockStore"/> interface.
    /// </summary>
    public static class IBlockStoreExt
    {
        public static IEnumerable<(ChainedHeader, Block)> BatchBlocksFrom(this IBlockStore blockStore, ChainedHeader previousBlock, ChainIndexer chainIndexer, CancellationTokenSource cancellationToken = null, int batchSize = 100)
        {
            for (int height = previousBlock.Height + 1; !(cancellationToken?.IsCancellationRequested ?? false);)
            {
                var hashes = new List<uint256>();
                for (int i = 0; i < batchSize; i++)
                {
                    ChainedHeader header = chainIndexer.GetHeader(height + i);
                    if (header == null)
                        break;

                    if (header.Previous != previousBlock)
                        break;

                    hashes.Add(header.HashBlock);

                    previousBlock = header;
                }

                if (hashes.Count == 0)
                    yield break;

                List<Block> blocks = blockStore.GetBlocks(hashes);

                for (int i = 0; i < blocks.Count && !(cancellationToken?.IsCancellationRequested ?? false); height++, i++)
                {
                    ChainedHeader header = chainIndexer.GetHeader(height);
                    yield return ((header, blocks[i]));
                }
            }
        }
    }
}
