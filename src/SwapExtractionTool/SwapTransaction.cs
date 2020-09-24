using NBitcoin;

namespace SwapExtractionTool
{
    public sealed class SwapTransaction
    {
        public SwapTransaction() { }

        public SwapTransaction(SwapTransaction swapTransaction)
        {
            this.BlockHeight = swapTransaction.BlockHeight;
            this.StraxAddress = swapTransaction.StraxAddress;
            this.SenderAmount = swapTransaction.SenderAmount;
            this.TransactionHash = swapTransaction.TransactionHash;
        }

        public int BlockHeight { get; set; }
        public string StraxAddress { get; set; }
        public Money SenderAmount { get; set; }
        public string TransactionHash { get; set; }
        public bool TransactionBuilt { get; set; }
        public bool TransactionSent { get; set; }
        public string TransactionSentHash { get; set; }
    }
}