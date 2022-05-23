using NBitcoin;
using Stratis.Bitcoin.P2P.Protocol.Payloads;
using Stratis.Features.FederatedPeg.Conversion;

namespace Stratis.Bitcoin.Features.Interop.Payloads
{
    [Payload("convrqst")]
    public sealed class ConversionRequestStatePayload : Payload
    {
        private bool isRequesting;
        private string requestId;
        private int requestState;
        private string signature;

        /// <summary>
        /// <c>True</c> if this payload is requesting a conversion request state from the other node.
        /// <c>False</c> if it is replying.
        /// </summary>
        public bool IsRequesting { get { return this.isRequesting; } }

        public string RequestId { get { return this.requestId; } }

        public ConversionRequestStatus RequestState { get { return (ConversionRequestStatus)this.requestState; } set { this.requestState = (int)value; } }

        public string Signature { get { return this.signature; } }

        /// <summary>Parameterless constructor needed for deserialization.</summary>
        public ConversionRequestStatePayload()
        {
        }

        private ConversionRequestStatePayload(string requestId, string signature, bool isRequesting)
        {
            this.requestId = requestId;
            this.signature = signature;
            this.isRequesting = isRequesting;
        }

        private ConversionRequestStatePayload(string requestId, int requestState, string signature, bool isRequesting)
        {
            this.requestId = requestId;
            this.requestState = requestState;
            this.signature = signature;
            this.isRequesting = isRequesting;
        }

        /// <inheritdoc/>
        public override void ReadWriteCore(BitcoinStream stream)
        {
            stream.ReadWrite(ref this.requestId);
            stream.ReadWrite(ref this.requestState);
            stream.ReadWrite(ref this.isRequesting);
        }

        public static ConversionRequestStatePayload Request(string requestId, string signature)
        {
            return new ConversionRequestStatePayload(requestId, signature, true);
        }

        public static ConversionRequestStatePayload Reply(string requestId, ConversionRequestStatus requestState, string signature)
        {
            return new ConversionRequestStatePayload(requestId, (int)requestState, signature, false);
        }

        /// <inheritdoc/>
        public override string ToString()
        {
            return $"{nameof(this.Command)}:'{this.Command}',{nameof(this.RequestId)}:'{this.RequestId}'";
        }
    }
}
