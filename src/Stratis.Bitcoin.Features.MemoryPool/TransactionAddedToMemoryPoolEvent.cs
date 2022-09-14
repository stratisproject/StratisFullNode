using NBitcoin;
using Stratis.Bitcoin.EventBus;
using Stratis.Bitcoin.Features.MemoryPool.Interfaces;

namespace Stratis.Bitcoin.Features.MemoryPool
{
    /// <summary>
    /// Event that is executed when a transaction is removed from the mempool.
    /// </summary>
    /// <seealso cref="EventBase" />
    public class TransactionAddedToMemoryPoolEvent : EventBase
    {
        public Transaction AddedTransaction { get; }
        public readonly long MemPoolSize;
       
       public TransactionAddedToMemoryPoolEvent(Transaction addedTransaction, long mempoolSize)
        {
            this.AddedTransaction = addedTransaction;
            this.MemPoolSize = mempoolSize;
        }        
    }
}