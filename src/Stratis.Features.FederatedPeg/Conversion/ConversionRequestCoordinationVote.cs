using System;
using System.Numerics;
using NBitcoin;
using Stratis.Bitcoin.Persistence;

namespace Stratis.Features.FederatedPeg.Conversion
{
    public class ConversionRequestCoordinationVote : IBitcoinSerializable
    {
        public string RequestId { get { return this.requestId; } set { this.requestId = value; } }

        public BigInteger TransactionId { get { return BigInteger.Parse(this.transactionId); } set { this.transactionId = value.ToString(); } }

        public PubKey PubKey { get { return this.pubKey; } set { this.pubKey = value; } }

        private string requestId;

        // BigInteger is not IBitcoinSerializable so we store its string representation instead.
        private string transactionId;

        private PubKey pubKey;

        public void ReadWrite(BitcoinStream stream)
        {
            stream.ReadWrite(ref this.requestId);
            stream.ReadWrite(ref this.transactionId);
            stream.ReadWrite(ref this.pubKey);
        }
    }
}
