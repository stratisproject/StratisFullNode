using NBitcoin;
using Stratis.Bitcoin.P2P.Protocol.Payloads;

namespace Stratis.Features.FederatedPeg.Payloads
{
    [Payload("feeproposal")]
    public sealed class FeeProposalPayload : Payload
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
        public FeeProposalPayload()
        {
        }

        private FeeProposalPayload(string requestId, ulong feeAmount, int height, string signature, bool isRequesting)
        {
            this.requestId = requestId;
            this.feeAmount = feeAmount;
            this.height = height;
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

        public override string ToString()
        {
            return $"{nameof(this.Command)}:'{this.Command}',{nameof(this.RequestId)}:'{this.RequestId}',{nameof(this.FeeAmount)}:'{this.FeeAmount}'";
        }

        public static FeeProposalPayload Request(string requestId, ulong feeAmount, int blockHeight, string signature)
        {
            return new FeeProposalPayload(requestId, feeAmount, blockHeight, signature, true);
        }

        public static FeeProposalPayload Reply(string requestId, ulong feeAmount, int blockHeight, string signature)
        {
            return new FeeProposalPayload(requestId, feeAmount, blockHeight, signature, false);
        }
    }
}
