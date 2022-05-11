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
        private readonly ITxMempool memPool;
        public long MemPoolSize { get; set; }
        public TransactionAddedToMemoryPoolEvent(Transaction addedTransaction, ITxMempool memPool)
        {
            this.AddedTransaction = addedTransaction;
            this.MemPoolSize = memPool.Size;
        }
        public TransactionAddedToMemoryPoolEvent(Transaction addedTransaction)
        {
            this.AddedTransaction = addedTransaction;            
        }
    }
}