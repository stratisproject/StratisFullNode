using System.Collections.Generic;
using NBitcoin;

namespace Stratis.Features.Unity3dApi
{
    public class GetUTXOsResponseModel
    {
        public long BalanceSat;

        public List<UTXOModel> UTXOs;

        public string Reason;
    }

    public class UTXOModel
    {
        public UTXOModel()
        {
        }

        public UTXOModel(OutPoint outPoint, Money value)
        {
            this.Hash = outPoint.Hash.ToString();
            this.N = outPoint.N;
            this.Satoshis = value.Satoshi;
        }

        public string Hash;

        public uint N;

        public long Satoshis;
    }

    public sealed class TipModel
    {
        public string TipHash { get; set; }

        public int TipHeight { get; set; }
    }

    public sealed class RawTxModel
    {
        public string Hex { get; set; }
    }
}
