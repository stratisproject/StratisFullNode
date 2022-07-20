using NBitcoin;
using Stratis.Bitcoin.Utilities;

namespace Stratis.Bitcoin.Features.SmartContracts.MetadataTracker
{
    public class MetadataTrackerEntry : IBitcoinSerializable
    {
        public HashHeightPair Block { get; set; }

        public uint256 TxId { get; set; }

        public string Metadata { get; set; }

        public void ReadWrite(BitcoinStream stream)
        {
            this.Block.ReadWrite(stream);
            var txId = this.TxId;
            stream.ReadWrite(ref txId);
            this.TxId = txId;
            var metadata = this.Metadata;
            stream.ReadWrite(ref metadata);
            this.Metadata = metadata;
        }
    }
}
