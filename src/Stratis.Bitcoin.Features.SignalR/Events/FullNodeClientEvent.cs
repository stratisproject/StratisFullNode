using System;
using Stratis.Bitcoin.EventBus;

namespace Stratis.Bitcoin.Features.SignalR.Events
{
    public sealed class FullNodeClientEvent : IClientEvent
    {
        public string Message { get; set; }

        public string State { get; set; }

        public Type NodeEventType { get; } = typeof(FullNodeEvent);

        public void BuildFrom(EventBase @event)
        {
            if (@event is FullNodeEvent progressEvent)
            {
                this.Message = progressEvent.Message;
                this.State = progressEvent.State;
                return;
            }

            throw new ArgumentException();
        }
    }
}
