using Stratis.Bitcoin.Consensus;
using Stratis.Bitcoin.Interfaces;

namespace Stratis.Bitcoin.Features.BlockStore
{
    /// <inheritdoc/>
    public sealed class BlockStoreQueueFlushCondition : IBlockStoreQueueFlushCondition
    {
        private readonly IInitialBlockDownloadState blockDownloadState;

        public BlockStoreQueueFlushCondition(IInitialBlockDownloadState blockDownloadState)
        {
            this.blockDownloadState = blockDownloadState;
        }

        /// <inheritdoc/>
        public bool ShouldFlush(IConsensusManager consensusManager, IBlockStoreQueue blockStoreQueue)
        {
            if (!consensusManager.IsAtBestChainTip(out _))
                return false;

            // If the node is in IBD we don't flush on each block.
            if (this.blockDownloadState.IsInitialBlockDownload())
                return false;

            // TODO: this code is not ideal it can be improved.
            // Checking the distance form tip is not ideal, it still leaves room for persisting single blocks when that is not desired.
            // For example if CT is not in IBD (node shutdown only for short time) but still needs to sync more then 100 blocks then we relay 
            // on a race condition that CT will validate faster then store can persists in order to move to batch based persistence.
            // The best fix is to pass in the dequeued block and check its height. 

            int distanceFromConsensusTip = consensusManager.Tip.Height - blockStoreQueue.StoreTip.Height;

            // Once store is less then 5 blocks form the consensus tip then flush on every block.
            if (distanceFromConsensusTip < 5)
                return true;

            return false;
        }
    }
}