using Stratis.Bitcoin.Primitives;

namespace Stratis.Bitcoin.EventBus.CoreEvents
{
    /// <summary>
    /// Event that is executed when voting data should be reconstructed for a given block.
    /// </summary>
    /// <seealso cref="EventBase" />
    public class ReconstructVoteDataForBlock : EventBase
    {
        public ChainedHeaderBlock ConnectedBlock { get; }

        public ReconstructVoteDataForBlock(ChainedHeaderBlock connectedBlock)
        {
            this.ConnectedBlock = connectedBlock;
        }
    }
}
