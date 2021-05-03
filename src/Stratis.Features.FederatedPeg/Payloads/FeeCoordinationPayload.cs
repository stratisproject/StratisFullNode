using NBitcoin;
using Stratis.Bitcoin.P2P.Protocol.Payloads;

namespace Stratis.Features.FederatedPeg.Payloads
{
    [Payload("feecoord")]
    public class FeeCoordinationPayload : Payload
    {
        private string requestId;
        private ulong feeAmount;
        private string signature;

        public string RequestId => this.requestId;

        public ulong FeeAmount => this.feeAmount;

        public string Signature => this.signature;

        /// <remarks>Needed for deserialization.</remarks>
        public FeeCoordinationPayload()
        {
        }

        public FeeCoordinationPayload(string requestId, ulong feeAmount, string signature)
        {
            this.requestId = requestId;
            this.feeAmount = feeAmount;
            this.signature = signature;
        }

        public override void ReadWriteCore(BitcoinStream stream)
        {
            stream.ReadWrite(ref this.requestId);
            stream.ReadWrite(ref this.feeAmount);
            stream.ReadWrite(ref this.signature);
        }

        public override string ToString()
        {
            return $"{nameof(this.Command)}:'{this.Command}',{nameof(this.RequestId)}:'{this.RequestId}',{nameof(this.FeeAmount)}:'{this.FeeAmount}'";
        }
    }
}
