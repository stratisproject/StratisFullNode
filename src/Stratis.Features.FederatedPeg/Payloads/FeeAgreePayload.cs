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
        private bool isRequesting;

        public int Height { get { return this.height; } }
        public string RequestId { get { return this.requestId; } }
        public ulong FeeAmount { get { return this.feeAmount; } }
        public string Signature { get { return this.signature; } }

        /// <summary>
        /// <c>True</c> if this payload is requesting a proposal from the other node.
        /// <c>False</c> if it is replying.
        /// </summary>
        public bool IsRequesting { get { return this.isRequesting; } }

        /// <summary>Needed for deserialization.</remarks>
        public FeeAgreePayload()
        {
        }

        private FeeAgreePayload(string requestId, ulong feeAmount, int height, string signature, bool isRequesting)
        {
            this.height = height;
            this.requestId = requestId;
            this.feeAmount = feeAmount;
            this.signature = signature;
            this.isRequesting = isRequesting;
        }

        public override void ReadWriteCore(BitcoinStream stream)
        {
            stream.ReadWrite(ref this.requestId);
            stream.ReadWrite(ref this.feeAmount);
            stream.ReadWrite(ref this.height);
            stream.ReadWrite(ref this.signature);
            stream.ReadWrite(ref this.isRequesting);
        }

        public static FeeAgreePayload Request(string requestId, ulong feeAmount, int blockHeight, string signature)
        {
            return new FeeAgreePayload(requestId, feeAmount, blockHeight, signature, true);
        }

        public static FeeAgreePayload Reply(string requestId, ulong feeAmount, int blockHeight, string signature)
        {
            return new FeeAgreePayload(requestId, feeAmount, blockHeight, signature, false);
        }

        public override string ToString()
        {
            return $"{nameof(this.Command)}:'{this.Command}',{nameof(this.RequestId)}:'{this.RequestId}',{nameof(this.FeeAmount)}:'{this.FeeAmount}'";
        }
    }
}
