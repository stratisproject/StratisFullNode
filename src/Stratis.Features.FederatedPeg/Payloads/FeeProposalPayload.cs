using NBitcoin;
using Stratis.Bitcoin.P2P.Protocol.Payloads;

namespace Stratis.Features.FederatedPeg.Payloads
{
    [Payload("feeproposal")]
    public class FeeProposalPayload : Payload
    {
        private string requestId;
        private ulong feeAmount;
        private string signature;

        public string RequestId { get { return this.requestId; } }
        public ulong FeeAmount { get { return this.feeAmount; } }
        public string Signature { get { return this.signature; } }

        /// <summary>Needed for deserialization.</remarks>
        public FeeProposalPayload()
        {
        }

        public FeeProposalPayload(string requestId, ulong feeAmount, string signature)
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
