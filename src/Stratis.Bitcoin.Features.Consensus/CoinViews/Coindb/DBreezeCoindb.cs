using System.Collections.Generic;
using Stratis.Bitcoin.Database;

namespace Stratis.Bitcoin.Features.Consensus.CoinViews
{
    public class DBreezeDbWithCoinDbNames : DBreezeDb
    {
        public DBreezeDbWithCoinDbNames() : base()
        {
            this.tableNames = new Dictionary<byte, string> {
                { 1, "Coins" },
                { 2, "BlockHash" },
                { 3, "Rewind" },
                { 4, "Stake" } };
        }
    }
}