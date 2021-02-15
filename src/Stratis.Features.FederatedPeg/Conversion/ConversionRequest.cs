using NBitcoin;

namespace Stratis.Bitcoin.Features.FederatedPeg
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
        private string requestId;

        private int requestType;

        private int requestStatus;

        private int blockHeight;

        private string destinationAddress;

        private ulong amount;

        private bool processed;

        /// <summary>
        /// The unique identifier for this particular conversion request.
        /// It gets selected by the request creator.
        /// The request ID is typically the initiating transaction ID.
        /// </summary>
        public string RequestId { get { return this.requestId; } set { this.requestId = value; } }

        public int RequestType { get { return this.requestType; } set { this.requestType = value; } }

        public int RequestStatus { get { return this.requestStatus; } set { this.requestStatus = value; } }

        /// <summary>
        /// For a mint request this is not really needed other than for informational purposes.
        /// However, a burn request needs to be scheduled for a future block on the main chain
        /// so that it can be cleanly inserted into the sequence of transfers.
        /// </summary>
        public int BlockHeight { get { return this.blockHeight; } set { this.blockHeight = value; } }

        /// <summary>
        /// Either the Ethereum address to send the minted funds to, or the STRAX address to send unwrapped wSTRAX funds to.
        /// </summary>
        public string DestinationAddress { get { return this.destinationAddress; } set { this.destinationAddress = value; } }

        /// <summary>
        /// Amount of the conversion, in satoshi. Conversions are currently processed 1:1.
        /// </summary>
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
            s.ReadWrite(ref this.blockHeight);
            s.ReadWrite(ref this.destinationAddress);
            s.ReadWrite(ref this.amount);
            s.ReadWrite(ref this.processed);
        }
    }
}
