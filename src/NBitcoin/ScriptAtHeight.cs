namespace NBitcoin
{
    public class ScriptAtHeight : Script
    {
        public ScriptAtHeight(Script script, int blockHeight, uint256 blockHash) : base()
        {
            this._Script = script._Script;
            this.BlockHeight = blockHeight;
            this.BlockHash = blockHash;
        }

        public uint256 BlockHash { get; private set; }
        public int BlockHeight { get; private set; }
    }
}
