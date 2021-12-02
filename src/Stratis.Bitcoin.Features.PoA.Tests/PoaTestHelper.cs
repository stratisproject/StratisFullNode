using System;
using System.Linq;
using NBitcoin;
using Stratis.Bitcoin.Primitives;
using Stratis.Bitcoin.Tests.Common;

namespace Stratis.Bitcoin.Features.PoA.Tests
{
    public static class PoaTestHelper
    {
        public static ChainedHeaderBlock[] GetEmptyBlocks(ChainIndexer chainIndexer, PoANetwork network, int count)
        {
            return GetBlocks(count, chainIndexer, i => CreateBlock(network, chainIndexer.Tip.Height + 1), chainIndexer.Tip);
        }

        public static ChainedHeaderBlock[] GetBlocks(int count, ChainIndexer chainIndexer, Func<int, ChainedHeaderBlock> block, ChainedHeader previousHeader = null)
        {
            ChainedHeader previous = null;

            if (previousHeader != null)
                previous = previousHeader;

            return Enumerable.Range(0, count).Select(i =>
            {
                ChainedHeaderBlock chainedHeaderBlock = block(i);
                chainedHeaderBlock.ChainedHeader.SetPrivatePropertyValue("Previous", previous);
                previous = chainedHeaderBlock.ChainedHeader;
                chainIndexer.Add(chainedHeaderBlock.ChainedHeader);
                return chainedHeaderBlock;
            }).ToArray();
        }

        public static ChainedHeaderBlock CreateBlock(PoANetwork network, int height)
        {
            Block block = CreateBlock(network, network.CreateTransaction(), height);
            return new ChainedHeaderBlock(block, new ChainedHeader(block.Header, block.GetHash(), height));
        }

        public static Block CreateBlock(PoANetwork network, Transaction transaction, int height)
        {
            Block block = new Block();

            if (transaction != null)
                block.Transactions.Add(transaction);

            block.Header.Time = (uint)(height * network.ConsensusOptions.TargetSpacingSeconds);

            block.UpdateMerkleRoot();
            block.GetHash();

            return block;
        }
    }
}