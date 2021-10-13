using System;
using System.Collections.Generic;
using System.Linq;
using NBitcoin;
using Stratis.Bitcoin.Consensus;
using Stratis.Bitcoin.Utilities;

namespace Stratis.Bitcoin.Features.PoA
{
    /// <summary>
    /// Implements a strategy that favors filling ALL mining slots even if requiring a "small" rewind.
    /// </summary>
    public class PoAChainWorkComparer : Comparer<ChainedHeader>, IChainWorkComparer
    {
        public const int MaximumRewindBlocks = 3;

        private readonly Network network;
        private readonly ISlotsManager slotsManager;
        private readonly IDateTimeProvider dateTimeProvider;

        public PoAChainWorkComparer(Network network, ISlotsManager slotsManager, IDateTimeProvider dateTimeProvider)
        {
            this.network = network;
            this.slotsManager = slotsManager;
            this.dateTimeProvider = dateTimeProvider;
        }

        public TimeSpan BlockProductionTime()
        {
            return TimeSpan.FromSeconds(((PoAConsensusOptions)this.network.Consensus.Options).TargetSpacingSeconds);
        }

        public uint GetNextMineableSlot()
        {
            uint timeNow = (uint)this.dateTimeProvider.GetAdjustedTimeAsUnixTimestamp();

            return this.slotsManager.GetMiningTimestamp(timeNow);
        }

        public override int Compare(ChainedHeader headerA, ChainedHeader headerB)
        {
            if (headerA.HashBlock == headerB.HashBlock)
                return 0;

            // A strategy that favors filling ALL mining slots takes priority for the last few blocks.
            // Chain A: A B C | - E F
            // Chain B: A B C | D <= WINNER

            TimeSpan maximumRewindSeconds = BlockProductionTime() * MaximumRewindBlocks;
            DateTimeOffset lastPermBlockA = headerA.Header.BlockTime - maximumRewindSeconds;
            DateTimeOffset lastPermBlockB = headerB.Header.BlockTime - maximumRewindSeconds;
            ChainedHeader[] lastOfA = headerA.EnumerateToGenesis().TakeWhile(h => h.Height > 0 && h.Header.BlockTime >= lastPermBlockA).Reverse().ToArray();
            ChainedHeader[] lastOfB = headerB.EnumerateToGenesis().TakeWhile(h => h.Height > 0 && h.Header.BlockTime >= lastPermBlockB).Reverse().ToArray();

            // Longest chain (excluding the rewindable blocks) wins.
            int cmp = (headerA.Height - lastOfA.Length).CompareTo(headerB.Height - lastOfB.Length);
            if (cmp != 0)
                return cmp;

            // Non-rewindable blocks are the same length.
            // Otherwise the chain containing the first earlier block is the winner.
            int min = Math.Min(lastOfA.Length, lastOfB.Length);
            for (int i = 0; i < min; i++)
            {
                int cmp2 = lastOfA[i].Header.BlockTime.CompareTo(lastOfB[i].Header.BlockTime);
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