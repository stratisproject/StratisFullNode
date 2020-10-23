namespace Stratis.Bitcoin.Features.BlockStore.Models
{
    public class LastBalanceDecreaseTransactionModel
    {
        public string TransactionHex { get; set; }

        public int BlockHeight { get; set; }

        //public long DecreasedBy { get; set; }
    }
}
