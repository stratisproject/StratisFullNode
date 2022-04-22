using Stratis.Bitcoin.EventBus;
using Stratis.Bitcoin.Primitives;

namespace Stratis.Bitcoin.Features.PoA.Voting
{
    /// <summary>
    /// Event that is executed when a block is disconnected from a consensus chain.
    /// </summary>
    /// <seealso cref="Stratis.Bitcoin.EventBus.EventBase" />
    public class VMProcessBlock : EventBase
    {
        public ChainedHeaderBlock ConnectedBlock { get; }
        public DBreeze.Transactions.Transaction PollsRepositoryTransaction { get; }

        public VMProcessBlock(ChainedHeaderBlock connectedBlock, DBreeze.Transactions.Transaction pollsRepositoryTransaction = null)
        {
            this.ConnectedBlock = connectedBlock;
            this.PollsRepositoryTransaction = pollsRepositoryTransaction;
        }
    }
}
