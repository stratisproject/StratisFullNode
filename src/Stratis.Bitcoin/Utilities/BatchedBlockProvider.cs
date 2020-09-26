using System.Collections.Generic;
using System.Threading;
using NBitcoin;
using Stratis.Bitcoin.Interfaces;

namespace Stratis.Bitcoin.Utilities
{
    public interface IBatchedBlockProvider
    {
        IEnumerable<(ChainedHeader, Block)> BatchBlocksFrom(int startHeight, CancellationTokenSource cancellationToken);
    }

    public class BatchedBlockProvider : IBatchedBlockProvider
    {
        private readonly ChainIndexer chainIndexer;
        private readonly IBlockStore blockStore;

        public BatchedBlockProvider(ChainIndexer chainIndexer, IBlockStore blockStore)
        {
            this.chainIndexer = chainIndexer;
            this.blockStore = blockStore;
        }

        public IEnumerable<(ChainedHeader, Block)> BatchBlocksFrom(int startHeight, CancellationTokenSource cancellationToken)
        {
            for (int height = startHeight; !cancellationToken.IsCancellationRequested; )
            {
                var hashes = new List<uint256>();
                for (int i = 0; i < 100; i++)
                {
                    ChainedHeader header = this.chainIndexer.GetHeader(height + i);
                    if (header == null)
                        break;

                    hashes.Add(header.HashBlock);
                }

                if (hashes.Count == 0)
                    yield break;

                List<Block> blocks = this.blockStore.GetBlocks(hashes);

                for (int i = 0; i < blocks.Count && !cancellationToken.IsCancellationRequested; height++, i++)
                {
                    ChainedHeader header = this.chainIndexer.GetHeader(height);
                    yield return ((header, blocks[i]));
                }
            }
        }
    }
}
