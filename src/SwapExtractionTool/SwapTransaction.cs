namespace SwapExtractionTool
{
    public sealed class SwapTransaction
    {
        public long BlockHeight { get; set; }
        public string StraxAddress { get; set; }
        public decimal SenderAmount { get; set; }
        public string TransactionHash { get; set; }
    }
}