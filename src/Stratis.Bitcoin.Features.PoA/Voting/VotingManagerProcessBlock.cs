using Stratis.Bitcoin.EventBus;
using Stratis.Bitcoin.Primitives;

namespace Stratis.Bitcoin.Features.PoA.Voting
{
    /// <summary>
    /// Event that is raised from the voting manager when a block is processed.
    /// <para>The event is consumed by the <see cref="JoinFederationRequestMonitor"/></para>
    /// </summary>
    /// <seealso cref="EventBase" />
    public sealed class VotingManagerProcessBlock : EventBase
    {
        public ChainedHeaderBlock ConnectedBlock { get; }
        public DBreeze.Transactions.Transaction PollsRepositoryTransaction { get; }

        public VotingManagerProcessBlock(ChainedHeaderBlock connectedBlock, DBreeze.Transactions.Transaction pollsRepositoryTransaction = null)
        {
            this.ConnectedBlock = connectedBlock;
            this.PollsRepositoryTransaction = pollsRepositoryTransaction;
        }
    }
}