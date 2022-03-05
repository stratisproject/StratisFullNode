﻿using System.Threading;
using NBitcoin;
using Stratis.Bitcoin.Consensus;
using Stratis.Bitcoin.Primitives;

namespace Stratis.Bitcoin.Interfaces
{
    public interface IBlockStoreQueue : IBlockStore
    {
        void ReindexChain(IConsensusManager consensusManager, CancellationToken nodeCancellation);

        /// <summary>Adds a block to the saving queue.</summary>
        /// <param name="chainedHeaderBlock">The block and its chained header pair to be added to pending storage.</param>
        void AddToPending(ChainedHeaderBlock chainedHeaderBlock);

        /// <summary>The highest stored block in the block store cache or <c>null</c> if block store feature is not enabled.</summary>
        ChainedHeader BlockStoreCacheTip { get; }

        /// <summary>The highest stored block in the repository.</summary>
        public ChainedHeader StoreTip { get; }

        /// <summary>
        /// Used by the <see cref="ConsensusManager"/> constructor to make itself known to ite dependency.
        /// </summary>
        /// <param name="consensusManager">The <see cref="IConsensusManager"/> of which this is a dependency.</param>
        void SetConsensusManager(IConsensusManager consensusManager);
    }
}
