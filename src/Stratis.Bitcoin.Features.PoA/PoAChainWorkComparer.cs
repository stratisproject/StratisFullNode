using System;
using System.Collections.Generic;
using System.Linq;
using NBitcoin;
using Stratis.Bitcoin.Consensus;

namespace Stratis.Bitcoin.Features.PoA
{
    /// <summary>
    /// Implements a strategy that favors filling ALL mining slots even if requiring a "small" rewind.
    /// </summary>
    public class PoAChainWorkComparer : Comparer<ChainedHeader>, IChainWorkComparer
    {
        private readonly Network network;

        public PoAChainWorkComparer(Network network)
        {
            this.network = network;
        }

        public TimeSpan BlockProductionTime()
        {
            return TimeSpan.FromSeconds(((PoAConsensusOptions)this.network.Consensus.Options).TargetSpacingSeconds);
        }

        public override int Compare(ChainedHeader headerA, ChainedHeader headerB)
        {
            // TODO: This could be derived from maximum block age.
            const int maximumRewindBlocks = 3;

            if (headerA.HashBlock == headerB.HashBlock)
                return 0;

            if (headerA.Previous?.HashBlock == headerB.HashBlock)
                return 1;

            if (headerA.HashBlock == headerB.Previous?.HashBlock)
                return -1;

            // A strategy that favors filling ALL mining slots takes priority for the last few blocks.
            // Chain A: A B C | - E F
            // Chain B: A B C | D <= WINNER

            TimeSpan maximumRewindSeconds = BlockProductionTime() * maximumRewindBlocks;
            DateTimeOffset lastPermBlockA = headerA.Header.BlockTime - maximumRewindSeconds;
            DateTimeOffset lastPermBlockB = headerB.Header.BlockTime - maximumRewindSeconds;
            ChainedHeader[] lastOfA = headerA.EnumerateToGenesis().TakeWhile(h => h.Height > 0 && h.Header.BlockTime >= lastPermBlockA).Reverse().ToArray();
            ChainedHeader[] lastOfB = headerB.EnumerateToGenesis().TakeWhile(h => h.Height > 0 && h.Header.BlockTime >= lastPermBlockB).Reverse().ToArray();

            // Longest chain (excluding the rewindable blocks) wins.
            int cmp = (headerA.Height - lastOfA.Length).CompareTo(headerB.Height - lastOfB.Length);
            if (cmp != 0)
                return cmp;

            // Non-rewindable blocks are the same length.
            // If only one chain has rewindable blocks then that chain is the winner.
            if ((lastOfA.Length == 0) != (lastOfB.Length == 0))
                return (lastOfA.Length == 0) ? -1 : 1;

            // Otherwise the chain containing the first earlier block is the winner.
            int min = Math.Min(lastOfA.Length, lastOfB.Length);
            for (int i = 0; i < min; i++)
            {
                int cmp2 = lastOfA[0].Header.BlockTime.CompareTo(lastOfB[0].Header.BlockTime);
                if (cmp2 != 0)
                    return -cmp2;
            }

            // If we're still tied then the chain with the most rewindable blocks wins.
            cmp = lastOfA.Length.CompareTo(lastOfB.Length);
            if (cmp != 0)
                return cmp;

            // Everything is equal. Resolve the tie by using the hash of the last block.
            return uint256.Comparison(headerA.HashBlock, headerB.HashBlock);
        }
    }
}
