using NBitcoin;
using Stratis.Bitcoin.P2P.Protocol.Payloads;

namespace Stratis.Bitcoin.Features.SmartContracts.Interop.Payloads
{
    [Payload("coord")]
    public class InteropCoordinationPayload : Payload
    {
        private string requestId;
        private int transactionId;

        public string RequestId => this.requestId;
        public int TransactionId => this.transactionId;

        /// <remarks>Needed for deserialization.</remarks>
        public InteropCoordinationPayload()
        {
        }

        public InteropCoordinationPayload(string requestId, int transactionId)
        {
            this.requestId = requestId;
            this.transactionId = transactionId;
        }

        public override void ReadWriteCore(BitcoinStream stream)
        {
            stream.ReadWrite(ref this.requestId);
            stream.ReadWrite(ref this.transactionId);
        }

        public override string ToString()
        {
            return $"{nameof(this.Command)}:'{this.Command}',{nameof(this.RequestId)}:'{this.RequestId}',{nameof(this.TransactionId)}:'{this.TransactionId}'";
        }
    }
}
