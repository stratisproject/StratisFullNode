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

        // States particular to Mint transactions
        OriginatorNotSubmitted,
        OriginatorSubmitted,
        VoteFinalised,
        NotOriginator,

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

        /// <summary>
        /// The type of the conversion request, mint or burn.
        /// </summary>
        public int RequestType { get { return this.requestType; } set { this.requestType = value; } }

        /// <summary>
        /// The status of the request, from unprocessed to processed.
        /// </summary>
        public int RequestStatus { get { return this.requestStatus; } set { this.requestStatus = value; } }

        /// <summary>
        /// For a mint request this is needed to coordinate which multisig member is considered the transaction originator on the wallet contract.
        /// A burn request needs to be scheduled for a future block on the main chain so that the conversion can be cleanly inserted into the sequence
        /// of transfers.
        /// </summary>
        public int BlockHeight { get { return this.blockHeight; } set { this.blockHeight = value; } }

        /// <summary>
        /// Either the Ethereum address to send the minted funds to, or the STRAX address to send unwrapped wSTRAX funds to.
        /// </summary>
        public string DestinationAddress { get { return this.destinationAddress; } set { this.destinationAddress = value; } }

        /// <summary>
        /// Amount of the conversion, this is always denominated in satoshi. This needs to be converted to wei for submitting mint transactions.
        /// Burn transactions are already denominated in wei on the Ethereum chain and thus need to be converted back into satoshi when the
        /// conversion request is created. Conversions are currently processed 1 ether : 1 STRAX.
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
