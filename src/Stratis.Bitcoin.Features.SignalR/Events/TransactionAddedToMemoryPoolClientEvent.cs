using System;
using System.Collections.Generic;
using System.Text;
using Stratis.Bitcoin.EventBus;
using Stratis.Bitcoin.Features.MemoryPool;

namespace Stratis.Bitcoin.Features.SignalR.Events
{
    public class TransactionAddedToMemoryPoolClientEvent : IClientEvent
    {
        public long MemPoolSize { get; set; }
        public Type NodeEventType { get; } = typeof(TransactionAddedToMemoryPoolEvent);
        public void BuildFrom(EventBase @event)
        {
            if (@event is TransactionAddedToMemoryPoolEvent transactionAddedToMemoryPool)
            {
                this.MemPoolSize = transactionAddedToMemoryPool.MemPoolSize;
                return;
            }

            throw new ArgumentException();
        }
    }
}
