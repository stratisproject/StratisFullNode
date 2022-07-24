namespace Stratis.Bitcoin.Features.OpenBanking.OpenBanking
{
    public class OBAmount
    {
        public string Amount { get; set; }
        public string Currency { get; set; }
    }

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

    public class OBTransactionData
    {
        public OBTransaction[] Transaction { get; set; }
    }

    public class OBGetTransactionsResponse
    {
        public OBTransactionData Data { get; set; }
    }
}
