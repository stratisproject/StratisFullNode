using System;
using Stratis.Bitcoin.EventBus;
using Stratis.Bitcoin.Features.Wallet.Events;

namespace Stratis.Bitcoin.Features.SignalR.Events
{
    public sealed class WalletProcessedTransactionOfInterestClientEvent : IClientEvent
    {
        public Type NodeEventType { get; } = typeof(WalletProcessedTransactionOfInterestEvent);

        public void BuildFrom(EventBase @event)
        {
            if (@event is WalletProcessedTransactionOfInterestEvent progressEvent)
                return;

            throw new ArgumentException();
        }
    }
}
