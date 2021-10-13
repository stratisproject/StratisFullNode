using System.Collections.Generic;
using NBitcoin;

namespace Stratis.Bitcoin.Consensus
{
    public interface IChainWorkComparer
    {
        int Compare(ChainedHeader headerA, ChainedHeader headerB);
        uint GetNextMineableSlot();
    }

    public class ChainWorkComparer : Comparer<ChainedHeader>, IChainWorkComparer
    {
        public override int Compare(ChainedHeader headerA, ChainedHeader headerB)
        {
            return uint256.Comparison(headerA.ChainWork, headerB.ChainWork);
        }

        public uint GetNextMineableSlot()
        {
            return uint.MaxValue;
        }
    }
}
