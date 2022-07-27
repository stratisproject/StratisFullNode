namespace Stratis.Bitcoin.Features.OpenBanking.OpenBanking
{
    /// <summary>
    /// Represents an amount as returned by the Open Banking API.
    /// </summary>
    public class OBAmount
    {
        public string Amount { get; set; }
        public string Currency { get; set; }
    }

    /// <summary>
    /// Represents a transaction as returned by the Open Banking API.
    /// </summary>
    public class OBTransaction
    {
        public string AccountId { get; set; }
        public string CreditDebitIndicator { get; set; }
        public string Status { get; set; }
        public string BookingDateTime { get; set; }
        public string ValueDateTime { get; set; }
        public string TransactionId { get; set; }
        public string TransactionReference { get; set; }
        public OBAmount Amount { get; set; }
    }

    /// <summary>
    /// Represents the "Data" as returned by the Open Banking API.
    /// </summary>
    public class OBTransactionData
    {
        public OBTransaction[] Transaction { get; set; }
    }

    /// <summary>
    /// Represents the response as returned by the Open Banking API.
    /// </summary>
    public class OBGetTransactionsResponse
    {
        public OBTransactionData Data { get; set; }
    }
}
