using NBitcoin;
using Stratis.Bitcoin.EventBus;

namespace Stratis.Bitcoin.Features.MemoryPool
{
    /// <summary>
    /// Event that is raised when a transaction fails any of the mempool rules.
    /// </summary>
    /// <seealso cref="EventBase" />
    public sealed class TransactionFailedMempoolValidation : EventBase
    {
        public readonly Transaction Transaction;

        public TransactionFailedMempoolValidation(Transaction tx)
        {
            this.Transaction = tx;
        }
    }
}