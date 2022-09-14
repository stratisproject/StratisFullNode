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
        private bool isTransfer;
        private bool isReplenishment;

        public string RequestId { get { return this.requestId; } }

        public int TransactionId { get { return this.transactionId; } }

        public string Signature { get { return this.signature; } }

        public DestinationChain DestinationChain { get { return (DestinationChain)this.destinationChain; } set { this.destinationChain = (int)value; } }

        /// <summary>
        /// <c>True</c> if this payload is requesting a proposal from the other node.
        /// <c>False</c> if it is replying.
        /// </summary>
        public bool IsRequesting { get { return this.isRequesting; } }

        /// <summary>
        /// <c>True</c> if the request in question is a transfer (e.g. ETH to Cirrus).
        /// <c>False</c> if not.
        /// </summary>
        public bool IsTransfer { get { return this.isTransfer; } }

        /// <summary>
        /// <c>True</c> if the request in question is for a wStrax replenishment transaction.
        /// <c>False</c> if not.
        /// </summary>
        public bool IsReplenishment { get { return this.isReplenishment; } }

        /// <summary>Parameterless constructor needed for deserialization.</summary>
        public ConversionRequestPayload()
        {
        }

        private ConversionRequestPayload(string requestId, int transactionId, string signature, DestinationChain destinationChain, bool isRequesting, bool isTransfer, bool isReplenishment)
        {
            this.requestId = requestId;
            this.transactionId = transactionId;
            this.signature = signature;
            this.destinationChain = (int)destinationChain;
            this.isRequesting = isRequesting;
            this.isTransfer = isTransfer;
            this.isReplenishment = isReplenishment;
        }

        /// <inheritdoc/>
        public override void ReadWriteCore(BitcoinStream stream)
        {
            stream.ReadWrite(ref this.requestId);
            stream.ReadWrite(ref this.transactionId);
            stream.ReadWrite(ref this.signature);
            stream.ReadWrite(ref this.destinationChain);
            stream.ReadWrite(ref this.isRequesting);
            stream.ReadWrite(ref this.isTransfer);
            stream.ReadWrite(ref this.isReplenishment);
        }

        public static ConversionRequestPayload Request(string requestId, int transactionId, string signature, DestinationChain destinationChain, bool isTransfer, bool isReplenishment)
        {
            return new ConversionRequestPayload(requestId, transactionId, signature, destinationChain, true, isTransfer, isReplenishment);
        }

        public static ConversionRequestPayload Reply(string requestId, int transactionId, string signature, DestinationChain destinationChain, bool isTransfer, bool isReplenishment)
        {
            return new ConversionRequestPayload(requestId, transactionId, signature, destinationChain, false, isTransfer, isReplenishment);
        }

        /// <inheritdoc/>
        public override string ToString()
        {
            return $"{nameof(this.Command)}:'{this.Command}',{nameof(this.RequestId)}:'{this.RequestId}',{nameof(this.TransactionId)}:'{this.TransactionId}'";
        }
    }
}
