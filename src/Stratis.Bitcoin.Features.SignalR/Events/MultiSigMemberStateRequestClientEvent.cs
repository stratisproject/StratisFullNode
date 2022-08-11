using System;
using Stratis.Bitcoin.EventBus;
using Stratis.Bitcoin.EventBus.CoreEvents;

namespace Stratis.Bitcoin.Features.SignalR.Events
{
    public class MultiSigMemberStateRequestClientEvent : IClientEvent
    {
        public int CrossChainStoreHeight { get; set; }
        public int CrossChainStoreNextDepositHeight { get; set; }
        public int PartialTransactions { get; set; }
        public int SuspendedPartialTransactions { get; set; }

        public Type NodeEventType { get; } = typeof(MultiSigMemberStateRequestEvent);

        public void BuildFrom(EventBase @event)
        {
            if (@event is MultiSigMemberStateRequestEvent multiSigState)
            {
                this.CrossChainStoreHeight = multiSigState.CrossChainStoreHeight;
                this.CrossChainStoreNextDepositHeight = multiSigState.CrossChainStoreNextDepositHeight;
                this.PartialTransactions = multiSigState.PartialTransactions;
                this.SuspendedPartialTransactions = multiSigState.SuspendedPartialTransactions;

                return;
            }

            throw new ArgumentException();
        }
    }
}
