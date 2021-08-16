using NBitcoin;
using Stratis.Bitcoin.Features.Wallet;
using Stratis.Bitcoin.P2P.Protocol.Payloads;

namespace Stratis.Bitcoin.Features.Interop.Payloads
{
    [Payload("convrequest")]
    public sealed class ConversionRequestPayload : Payload
    {
        private string requestId;
        private int transactionId;
        private string signature;
        private int destinationChain;
        private bool isRequesting;

        public string RequestId { get { return this.requestId; } }

        public int TransactionId { get { return this.transactionId; } }

        public string Signature { get { return this.signature; } }

        public DestinationChain DestinationChain { get { return (DestinationChain)this.destinationChain; } set { this.destinationChain = (int)value; } }

        /// <summary>
        /// <c>True</c> if this payload is requesting a proposal from the other node.
        /// <c>False</c> if it is replying.
        /// </summary>
        public bool IsRequesting { get { return this.isRequesting; } }

        /// <summary>Parameterless constructor needed for deserialization.</summary>
        public ConversionRequestPayload()
        {
        }

        private ConversionRequestPayload(string requestId, int transactionId, string signature, DestinationChain destinationChain, bool isRequesting)
        {
            this.requestId = requestId;
            this.transactionId = transactionId;
            this.signature = signature;
            this.destinationChain = (int)destinationChain;
            this.isRequesting = isRequesting;
        }

        /// <inheritdoc/>
        public override void ReadWriteCore(BitcoinStream stream)
        {
            stream.ReadWrite(ref this.requestId);
            stream.ReadWrite(ref this.transactionId);
            stream.ReadWrite(ref this.signature);
            stream.ReadWrite(ref this.destinationChain);
            stream.ReadWrite(ref this.isRequesting);
        }

        public static ConversionRequestPayload Request(string requestId, int transactionId, string signature, DestinationChain destinationChain)
        {
            return new ConversionRequestPayload(requestId, transactionId, signature, destinationChain, true);
        }

        public static ConversionRequestPayload Reply(string requestId, int transactionId, string signature, DestinationChain destinationChain)
        {
            return new ConversionRequestPayload(requestId, transactionId, signature, destinationChain, false);
        }

        /// <inheritdoc/>
        public override string ToString()
        {
            return $"{nameof(this.Command)}:'{this.Command}',{nameof(this.RequestId)}:'{this.RequestId}',{nameof(this.TransactionId)}:'{this.TransactionId}'";
        }
    }
}
