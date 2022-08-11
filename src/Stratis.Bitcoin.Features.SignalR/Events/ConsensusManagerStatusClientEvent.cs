using System;
using System.Collections.Generic;
using System.Text;
using Stratis.Bitcoin.EventBus;
using Stratis.Bitcoin.EventBus.CoreEvents;

namespace Stratis.Bitcoin.Features.SignalR.Events
{
    public class ConsensusManagerStatusClientEvent : IClientEvent
    {
        public bool IsIbd { get; set; }
        public Type NodeEventType { get; } = typeof(ConsensusManagerStatusEvent);

        public void BuildFrom(EventBase @event)
        {
            if (@event is ConsensusManagerStatusEvent consensusManagerStatusEvent)
            {
                this.IsIbd = consensusManagerStatusEvent.IsIbd;
                return;
            }
            throw new NotImplementedException();
        }
    }
}
