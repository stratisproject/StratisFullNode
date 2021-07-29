using NBitcoin;
using Stratis.Bitcoin.Features.Wallet;
using Stratis.Bitcoin.P2P.Protocol.Payloads;

namespace Stratis.Bitcoin.Features.Interop.Payloads
{
    [Payload("coord")]
    public class InteropCoordinationPayload : Payload
    {
        private string requestId;
        private int transactionId;
        private string signature;
        private int destinationChain;

        public string RequestId => this.requestId;

        public int TransactionId => this.transactionId;

        public string Signature => this.signature;

        public DestinationChain DestinationChain { get { return (DestinationChain)this.destinationChain; } set { this.destinationChain = (int)value; } }

        /// <remarks>Needed for deserialization.</remarks>
        public InteropCoordinationPayload()
        {
        }

        public InteropCoordinationPayload(string requestId, int transactionId, string signature, DestinationChain targetChain)
        {
            this.requestId = requestId;
            this.transactionId = transactionId;
            this.signature = signature;
            this.DestinationChain = targetChain;
        }

        public override void ReadWriteCore(BitcoinStream stream)
        {
            stream.ReadWrite(ref this.requestId);
            stream.ReadWrite(ref this.transactionId);
            stream.ReadWrite(ref this.signature);
            stream.ReadWrite(ref this.destinationChain);
        }

        public override string ToString()
        {
            return $"{nameof(this.Command)}:'{this.Command}',{nameof(this.RequestId)}:'{this.RequestId}',{nameof(this.TransactionId)}:'{this.TransactionId}'";
        }
    }
}
