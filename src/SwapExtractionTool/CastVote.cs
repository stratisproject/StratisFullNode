using NBitcoin;

namespace SwapExtractionTool
{
    public sealed class CastVote
    {
        public string Address { get; set; }
        public Money Balance { get; set; }
        public bool InFavour { get; set; }
        public int BlockHeight { get; set; }
    }
}