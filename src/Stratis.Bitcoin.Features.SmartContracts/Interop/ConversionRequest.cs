using NBitcoin;

namespace Stratis.Bitcoin.Features.SmartContracts.Interop
{
    public enum ConversionRequestType
    {
        Mint,
        Burn
    }

    public enum ConversionRequestStatus
    {
        Unprocessed,
        Submitted,
        Processed
    }

    public class ConversionRequest : IBitcoinSerializable
    {
        /// <summary>
        /// The request ID is typically the initiating transaction ID.
        /// </summary>
        private string requestId;

        private int requestType;

        private int requestStatus;

        /// <summary>
        /// Either the Ethereum address to send the minted funds to, or the Cirrus address to send funds to.
        /// </summary>
        private string destinationAddress;

        /// <summary>
        /// Amount of the conversion, in wei or satoshi.
        /// </summary>
        private ulong amount;

        private bool processed;

        /// <summary>
        /// The unique identifier for this particular remote contract invocation request.
        /// It gets selected by the request creator.
        /// </summary>
        public string RequestId { get { return this.requestId; } set { this.requestId = value; } }

        public int RequestType { get { return this.requestType; } set { this.requestType = value; } }

        public int RequestStatus { get { return this.requestStatus; } set { this.requestStatus = value; } }

        public string DestinationAddress { get { return this.destinationAddress; } set { this.destinationAddress = value; } }

        public ulong Amount { get { return this.amount; } set { this.amount = value; } }

        /// <summary>
        /// Indicates whether or not this request has been processed by the interop poller.
        /// </summary>
        public bool Processed { get { return this.processed; } set { this.processed = value; } }

        public void ReadWrite(BitcoinStream s)
        {
            s.ReadWrite(ref this.requestId);
            s.ReadWrite(ref this.requestType);
            s.ReadWrite(ref this.requestStatus);
            s.ReadWrite(ref this.destinationAddress);
            s.ReadWrite(ref this.amount);
            s.ReadWrite(ref this.processed);
        }
    }
}
