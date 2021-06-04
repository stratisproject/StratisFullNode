using NBitcoin;
using Stratis.Bitcoin.P2P.Protocol.Payloads;

namespace Stratis.Features.FederatedPeg.Payloads
{
    [Payload("feeagree")]
    public class FeeAgreePayload : Payload
    {
        private int height;
        private string requestId;
        private ulong feeAmount;
        private string signature;

        public int Height { get { return this.height; } }
        public string RequestId { get { return this.requestId; } }
        public ulong FeeAmount { get { return this.feeAmount; } }
        public string Signature { get { return this.signature; } }

        /// <summary>Needed for deserialization.</remarks>
        public FeeAgreePayload()
        {
        }

        public FeeAgreePayload(string requestId, ulong feeAmount, int height, string signature)
        {
            this.height = height;
            this.requestId = requestId;
            this.feeAmount = feeAmount;
            this.signature = signature;
        }

        public override void ReadWriteCore(BitcoinStream stream)
        {
            stream.ReadWrite(ref this.requestId);
            stream.ReadWrite(ref this.feeAmount);
            stream.ReadWrite(ref this.height);
            stream.ReadWrite(ref this.signature);
        }

        public override string ToString()
        {
            return $"{nameof(this.Command)}:'{this.Command}',{nameof(this.RequestId)}:'{this.RequestId}',{nameof(this.FeeAmount)}:'{this.FeeAmount}'";
        }
    }
}
