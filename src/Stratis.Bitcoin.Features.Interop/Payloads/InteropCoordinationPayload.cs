using NBitcoin;
using NBitcoin.Crypto;
using Stratis.Bitcoin.P2P.Protocol.Payloads;

namespace Stratis.Bitcoin.Features.Interop.Payloads
{
    [Payload("coord")]
    public class InteropCoordinationPayload : Payload
    {
        private string requestId;
        private int transactionId;
        private string signature;

        public string RequestId => this.requestId;
        
        public int TransactionId => this.transactionId;
        
        public string Signature => this.signature;

        /// <remarks>Needed for deserialization.</remarks>
        public InteropCoordinationPayload()
        {
        }

        public InteropCoordinationPayload(string requestId, int transactionId, string signature)
        {
            this.requestId = requestId;
            this.transactionId = transactionId;
            this.signature = signature;
        }

        public override void ReadWriteCore(BitcoinStream stream)
        {
            stream.ReadWrite(ref this.requestId);
            stream.ReadWrite(ref this.transactionId);
            stream.ReadWrite(ref this.signature);
        }

        public override string ToString()
        {
            return $"{nameof(this.Command)}:'{this.Command}',{nameof(this.RequestId)}:'{this.RequestId}',{nameof(this.TransactionId)}:'{this.TransactionId}'";
        }
    }
}
