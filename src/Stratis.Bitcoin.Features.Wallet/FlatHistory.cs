namespace Stratis.Bitcoin.Features.Wallet
{
    /// <summary>
    /// A class that represents a flat view of the wallets history.
    /// </summary>
    public class FlatHistory
    {
        /// <summary>
        /// The address associated with this UTXO.
        /// </summary>
        public HdAddress Address { get; set; }

        /// <summary>
        /// The transaction representing the UTXO.
        /// </summary>
        public TransactionData Transaction { get; set; }
    }

    /// <summary>
    /// A class that represents a flat view of the wallets history.
    /// </summary>
    public sealed class FlattenedHistoryItem
    {
        public string Id { get; set; }

        public int Type { get; set; }

        public long Amount { get; set; }

        public long Fee { get; set; }

        public long Timestamp { get; set; }

        /// <summary>
        /// The Base58 representation of this address.
        /// </summary>
        public string ToAddress { get; set; }

        /// <summary>
        /// The height of the block in which this transaction was confirmed.
        /// </summary>
        public int? BlockHeight { get; set; }
    }
}