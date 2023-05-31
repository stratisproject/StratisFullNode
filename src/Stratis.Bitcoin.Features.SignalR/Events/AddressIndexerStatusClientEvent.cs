using System;
using Stratis.Bitcoin.EventBus;
using Stratis.Bitcoin.EventBus.CoreEvents;

namespace Stratis.Bitcoin.Features.SignalR.Events
{
    public sealed class AddressIndexerStatusClientEvent : IClientEvent
    {
        public int Tip { get; set; }

        public Type NodeEventType { get; } = typeof(AddressIndexerStatusEvent);

        public void BuildFrom(EventBase @event)
        {
            if (@event is AddressIndexerStatusEvent addressIndexerStatusEvent)
            {
                this.Tip = addressIndexerStatusEvent.Tip;
                return;
            }

            throw new NotImplementedException();
        }
    }
}
